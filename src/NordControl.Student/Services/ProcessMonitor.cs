using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NordControl.Core.Helpers;
using NordControl.Protocol;

namespace NordControl.Student.Services;

public class ProcessMonitor
{
    private const int MaxTitleLength = 512;
    private const int GwlExStyle = -20;
    private const int GwOwner = 4;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExAppwindow = 0x00040000;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetProcessImageFileName(IntPtr hProcess, StringBuilder lpImageFileName, int nSize);

    public const int MaxItems = 40;

    public WireMessage CollectProcessList(int maxItems = MaxItems)
    {
        var activeExe = GetActiveProcessExe();
        var items = new List<ProcessItemInfo>();
        var seen = new HashSet<int>();

        try
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!IsAltTabWindow(hWnd))
                    {
                        return true;
                    }

                    var title = GetTitle(hWnd);
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(hWnd, out var pid);
                    if (pid == 0)
                    {
                        return true;
                    }

                    pid = ResolveWindowPid(hWnd, pid);

                    var exeName = GetProcessExe((int)pid);
                    if (string.IsNullOrEmpty(exeName) || ProcessListFilter.IsNoiseExe(exeName))
                    {
                        return true;
                    }

                    if (!seen.Add((int)pid))
                    {
                        return true;
                    }

                    items.Add(new ProcessItemInfo
                    {
                        Exe = exeName,
                        Pid = (int)pid,
                        Title = title
                    });
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        var truncatedItems = items
            .OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxItems)
            .ToList();

        return new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "process_list",
            ActiveExe = activeExe,
            Items = truncatedItems
        };
    }

    public IReadOnlyList<InstalledAppInfo> CollectWindowedApps(IReadOnlyList<InstalledAppInfo>? installed)
    {
        var windows = CollectProcessList(MaxItems);
        var byExe = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);
        if (installed != null)
        {
            foreach (var app in installed)
            {
                if (!string.IsNullOrWhiteSpace(app.Exe) && !byExe.ContainsKey(app.Exe))
                {
                    byExe[app.Exe] = app;
                }
            }
        }

        var result = new List<InstalledAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in windows.Items ?? new List<ProcessItemInfo>())
        {
            if (!seen.Add(item.Exe))
            {
                continue;
            }

            if (byExe.TryGetValue(item.Exe, out var known))
            {
                result.Add(new InstalledAppInfo
                {
                    Name = string.IsNullOrWhiteSpace(known.Name) ? item.Title : known.Name,
                    Exe = item.Exe,
                    LaunchTarget = known.LaunchTarget
                });
            }
            else
            {
                result.Add(new InstalledAppInfo
                {
                    Name = item.Title,
                    Exe = item.Exe
                });
            }
        }

        return result;
    }

    private static bool IsAltTabWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd))
        {
            return false;
        }

        if (GetWindow(hWnd, GwOwner) != IntPtr.Zero)
        {
            return false;
        }

        var ex = unchecked((int)(long)GetWindowLongPtr(hWnd, GwlExStyle));
        if ((ex & WsExToolwindow) != 0 && (ex & WsExAppwindow) == 0)
        {
            return false;
        }

        return true;
    }

    private static uint ResolveWindowPid(IntPtr hWnd, uint pid)
    {
        var hostExe = GetProcessExe((int)pid);
        if (!ProcessNameHelper.Equals(hostExe, "applicationframehost.exe"))
        {
            return pid;
        }

        uint resolved = pid;
        try
        {
            EnumChildWindows(hWnd, (child, _) =>
            {
                GetWindowThreadProcessId(child, out var childPid);
                if (childPid == 0 || childPid == pid)
                {
                    return true;
                }

                var childExe = GetProcessExe((int)childPid);
                if (string.IsNullOrEmpty(childExe) || ProcessListFilter.IsNoiseExe(childExe))
                {
                    return true;
                }

                resolved = childPid;
                return false;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return resolved;
    }

    private static string GetTitle(IntPtr hWnd)
    {
        var buffer = new StringBuilder(MaxTitleLength);
        _ = GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static string GetProcessExe(int pid)
    {
        var image = QueryProcessImageName(pid);
        if (!string.IsNullOrEmpty(image))
        {
            return ProcessNameHelper.Normalize(image);
        }

        try
        {
            using var proc = Process.GetProcessById(pid);
            string rawName;
            try
            {
                rawName = proc.MainModule?.FileName ?? proc.ProcessName;
            }
            catch
            {
                rawName = proc.ProcessName;
            }

            return ProcessNameHelper.Normalize(rawName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QueryProcessImageName(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
            {
                return buffer.ToString();
            }

            buffer.Clear();
            buffer.EnsureCapacity(1024);
            var deviceLen = GetProcessImageFileName(handle, buffer, buffer.Capacity);
            if (deviceLen > 0)
            {
                return buffer.ToString();
            }

            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? GetActiveProcessExe()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
            {
                return null;
            }

            pid = ResolveWindowPid(hwnd, pid);
            var exe = GetProcessExe((int)pid);
            return string.IsNullOrEmpty(exe) || ProcessListFilter.IsNoiseExe(exe) ? null : exe;
        }
        catch
        {
            return null;
        }
    }
}
