using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NordControl.Core.Helpers;
using NordControl.Protocol;

namespace NordControl.Student.Services;

public class ProcessMonitor
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public const int MaxItems = 40;

    public WireMessage CollectProcessList(int maxItems = MaxItems)
    {
        var activeExe = GetActiveProcessExe();
        var items = new List<ProcessItemInfo>();

        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero)
                        continue;

                    var title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title))
                        continue;

                    string rawName;
                    try
                    {
                        rawName = p.MainModule?.FileName ?? p.ProcessName;
                    }
                    catch
                    {
                        rawName = p.ProcessName;
                    }

                    var exeName = ProcessNameHelper.Normalize(rawName);

                    items.Add(new ProcessItemInfo
                    {
                        Exe = exeName,
                        Pid = p.Id,
                        Title = title
                    });
                }
                catch
                {
                    // Process might have exited or permission denied
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // Fallback if process enumeration fails
        }

        var truncatedItems = items.Take(maxItems).ToList();

        return new WireMessage
        {
            V = ProtocolConstants.Version,
            Type = "process_list",
            ActiveExe = activeExe,
            Items = truncatedItems
        };
    }

    private static string? GetActiveProcessExe()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return null;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return null;

            using var activeProc = Process.GetProcessById((int)pid);
            string rawName;
            try
            {
                rawName = activeProc.MainModule?.FileName ?? activeProc.ProcessName;
            }
            catch
            {
                rawName = activeProc.ProcessName;
            }

            var exe = ProcessNameHelper.Normalize(rawName);
            return string.IsNullOrEmpty(exe) ? null : exe;
        }
        catch
        {
            return null;
        }
    }
}
