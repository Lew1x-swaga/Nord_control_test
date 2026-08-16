using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using NordControl.Core.Helpers;
using NordControl.Protocol;

namespace NordControl.Core.Policies;

public class InstalledAppsScanner
{
    public IReadOnlyList<InstalledAppInfo> ScanInstalledApps()
    {
        var rawList = new List<InstalledAppInfo>();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ScanStartMenu(rawList);
                ScanRegistry(rawList);
            }
        }
        catch
        {
            // Fail safe, return whatever was collected
        }

        return FilterAndDeduplicate(rawList);
    }

    private static void ScanStartMenu(List<InstalledAppInfo> list)
    {
        var startMenuFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        foreach (var baseFolder in startMenuFolders)
        {
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                continue;
            }

            try
            {
                var shortcuts = Directory.EnumerateFiles(baseFolder, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in shortcuts)
                {
                    try
                    {
                        var name = Path.GetFileNameWithoutExtension(lnk);
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // Use the .lnk path as launch target if resolved, with an assumed .exe or fallback name
                        var exeName = ProcessNameHelper.Normalize(name);
                        list.Add(new InstalledAppInfo
                        {
                            Name = name,
                            Exe = exeName,
                            LaunchTarget = lnk
                        });
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ScanRegistry(List<InstalledAppInfo> list)
    {
        var registryPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        var rootKeys = new[] { Registry.LocalMachine, Registry.CurrentUser };

        foreach (var root in rootKeys)
        {
            foreach (var path in registryPaths)
            {
                try
                {
                    using var baseKey = root.OpenSubKey(path);
                    if (baseKey == null) continue;

                    foreach (var subKeyName in baseKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = baseKey.OpenSubKey(subKeyName);
                            if (appKey == null) continue;

                            var displayName = appKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            var displayIcon = appKey.GetValue("DisplayIcon") as string;
                            var installLocation = appKey.GetValue("InstallLocation") as string;

                            string? exeName = null;
                            string? launchTarget = null;

                            if (!string.IsNullOrWhiteSpace(displayIcon))
                            {
                                var iconPath = displayIcon.Trim().Trim('"');
                                var commaIndex = iconPath.IndexOf(',');
                                if (commaIndex > 0)
                                {
                                    iconPath = iconPath.Substring(0, commaIndex).Trim();
                                }

                                if (ProcessNameHelper.IsExe(iconPath) && File.Exists(iconPath))
                                {
                                    exeName = ProcessNameHelper.Normalize(iconPath);
                                    launchTarget = iconPath;
                                }
                            }

                            if (string.IsNullOrEmpty(exeName) && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                            {
                                try
                                {
                                    var mainExe = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                    if (mainExe != null)
                                    {
                                        exeName = ProcessNameHelper.Normalize(mainExe);
                                        launchTarget = mainExe;
                                    }
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(exeName))
                            {
                                exeName = ProcessNameHelper.Normalize(displayName);
                            }

                            list.Add(new InstalledAppInfo
                            {
                                Name = displayName.Trim(),
                                Exe = exeName,
                                LaunchTarget = launchTarget
                            });
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }

    public static IReadOnlyList<InstalledAppInfo> FilterAndDeduplicate(IEnumerable<InstalledAppInfo> apps)
    {
        var result = new List<InstalledAppInfo>();
        var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.Exe))
            {
                continue;
            }

            var exeNameOnly = ProcessNameHelper.Normalize(app.Exe);
            if (string.IsNullOrEmpty(exeNameOnly))
            {
                continue;
            }

            // Exclude uninstallers
            if (exeNameOnly.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                exeNameOnly.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase) ||
                app.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Exclude self and protected
            if (RamAppBlocker.IsProtected(exeNameOnly))
            {
                continue;
            }

            if (seenExes.Add(exeNameOnly))
            {
                result.Add(new InstalledAppInfo
                {
                    Name = app.Name.Trim(),
                    Exe = exeNameOnly,
                    LaunchTarget = app.LaunchTarget
                });
            }
        }

        return result;
    }
}
