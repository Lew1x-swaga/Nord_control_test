using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using NordControl.Core.Helpers;

namespace NordControl.Core.Policies;

public class AppLauncher : IAppLauncher
{
    private readonly Func<string, bool>? _startAction;

    private static readonly Dictionary<string, string> ProtocolAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calc.exe"] = "calculator:",
        ["calculator.exe"] = "calculator:",
        ["win32calc.exe"] = "calculator:",
    };

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
            if (LooksLikeFilePath(resolved) && !File.Exists(resolved))
            {
                return false;
            }

            if (TryStart(resolved))
            {
                return true;
            }

            if (TryStartViaCmd(resolved))
            {
                return true;
            }

            var protocol = TryProtocolAlias(exe) ?? TryProtocolAlias(target);
            if (!string.IsNullOrWhiteSpace(protocol) && !string.Equals(protocol, resolved, StringComparison.OrdinalIgnoreCase))
            {
                if (TryStart(protocol) || TryStartViaCmd(protocol))
                {
                    return true;
                }
            }

            if (!string.Equals(target, resolved, StringComparison.OrdinalIgnoreCase)
                && (TryStart(target) || TryStartViaCmd(target)))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveLaunchPath(string target, string? exe)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return target;
        }

        if (LooksLikeFilePath(target) || LooksLikeProtocol(target))
        {
            return target;
        }

        var alias = TryProtocolAlias(target) ?? TryProtocolAlias(exe);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            return alias;
        }

        var fromAppPaths = TryAppPaths(target) ?? TryAppPaths(ProcessNameHelper.Normalize(exe ?? target));
        return fromAppPaths ?? target;
    }

    internal static string? TryProtocolAlias(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (LooksLikeProtocol(name))
        {
            return name;
        }

        var normalized = ProcessNameHelper.Normalize(name);
        return ProtocolAliases.TryGetValue(normalized, out var alias) ? alias : null;
    }

    private static bool TryStart(string fileName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true
            };
            var process = Process.Start(psi);
            if (process != null)
            {
                return true;
            }

            return LooksLikeProtocol(fileName);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartViaCmd(string target)
    {
        if (!LooksLikeProtocol(target) && !File.Exists(target))
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"\" " + QuoteForCmd(target),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(2500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteForCmd(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static bool LooksLikeFilePath(string target)
    {
        if (target.Contains('\\', StringComparison.Ordinal) || target.Contains('/', StringComparison.Ordinal))
        {
            return true;
        }

        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            || target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || target.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || target.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return Path.IsPathRooted(target);
        }

        return target.Length >= 3
            && char.IsLetter(target[0])
            && target[1] == ':'
            && (target[2] == '\\' || target[2] == '/');
    }

    private static bool LooksLikeProtocol(string target)
    {
        var colon = target.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        if (LooksLikeFilePath(target))
        {
            return false;
        }

        return colon == target.Length - 1 || target[colon + 1] != '\\';
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
