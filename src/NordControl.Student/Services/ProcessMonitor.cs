using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

                    string exeName;
                    try
                    {
                        exeName = Path.GetFileName(p.MainModule?.FileName ?? p.ProcessName + ".exe");
                    }
                    catch
                    {
                        exeName = p.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? p.ProcessName
                            : p.ProcessName + ".exe";
                    }

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
            try
            {
                return Path.GetFileName(activeProc.MainModule?.FileName ?? activeProc.ProcessName + ".exe");
            }
            catch
            {
                return activeProc.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? activeProc.ProcessName
                    : activeProc.ProcessName + ".exe";
            }
        }
        catch
        {
            return null;
        }
    }
}
