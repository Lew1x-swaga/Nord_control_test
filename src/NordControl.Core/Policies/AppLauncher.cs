using System;
using System.Diagnostics;
using Microsoft.Win32;
using NordControl.Core.Helpers;

namespace NordControl.Core.Policies;

public class AppLauncher : IAppLauncher
{
    private readonly Func<string, bool>? _startAction;

    public AppLauncher(Func<string, bool>? startAction = null)
    {
        _startAction = startAction;
    }

    public bool Launch(string exe, string? launchTarget = null)
    {
        try
        {
            var target = !string.IsNullOrWhiteSpace(launchTarget)
                ? launchTarget.Trim()
                : (!string.IsNullOrWhiteSpace(exe) ? exe.Trim() : null);

            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (_startAction != null)
            {
                return _startAction(target);
            }

            var resolved = ResolveLaunchPath(target, exe);
            var psi = new ProcessStartInfo
            {
                FileName = resolved,
                UseShellExecute = true
            };

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveLaunchPath(string target, string? exe)
    {
        if (target.Contains('\\') || target.Contains('/'))
        {
            return target;
        }

        var fromAppPaths = TryAppPaths(target) ?? TryAppPaths(ProcessNameHelper.Normalize(exe ?? target));
        return fromAppPaths ?? target;
    }

    private static string? TryAppPaths(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var keyName = ProcessNameHelper.Normalize(fileName);
        string[] roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + keyName,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + keyName
        };

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var root in roots)
            {
                try
                {
                    using var key = hive.OpenSubKey(root);
                    var path = key?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path.Trim().Trim('"');
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }
}
