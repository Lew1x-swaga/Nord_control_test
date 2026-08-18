using System;
using System.Collections.Generic;
using System.Linq;
using NordControl.Core.Helpers;
using NordControl.Protocol;

namespace NordControl.Core.Policies;

public static class AppSuggestionFilter
{
    public const int DefaultLimit = 20;

    public static IReadOnlyList<InstalledAppInfo> Merge(params IEnumerable<InstalledAppInfo>?[] sources)
    {
        var result = new List<InstalledAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (source == null)
            {
                continue;
            }

            foreach (var app in source)
            {
                if (app == null || string.IsNullOrWhiteSpace(app.Exe))
                {
                    continue;
                }

                var exe = ProcessNameHelper.Normalize(app.Exe);
                if (string.IsNullOrWhiteSpace(exe) || !seen.Add(exe))
                {
                    continue;
                }

                result.Add(new InstalledAppInfo
                {
                    Name = string.IsNullOrWhiteSpace(app.Name) ? exe : app.Name.Trim(),
                    Exe = exe,
                    LaunchTarget = string.IsNullOrWhiteSpace(app.LaunchTarget) ? null : app.LaunchTarget.Trim()
                });
            }
        }

        return result;
    }

    public static IReadOnlyList<InstalledAppInfo> Filter(
        IEnumerable<InstalledAppInfo> catalog,
        string? query,
        int maxResults = DefaultLimit)
    {
        if (maxResults <= 0)
        {
            return Array.Empty<InstalledAppInfo>();
        }

        var items = catalog?.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Exe)).ToList()
                    ?? new List<InstalledAppInfo>();
        var needle = query?.Trim() ?? string.Empty;
        if (needle.Length == 0)
        {
            return items.Take(maxResults).ToList();
        }

        return items
            .Select(app => (app, rank: Rank(app, needle)))
            .Where(x => x.rank > 0)
            .OrderByDescending(x => x.rank)
            .ThenBy(x => x.app.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(maxResults)
            .Select(x => x.app)
            .ToList();
    }

    private static int Rank(InstalledAppInfo app, string query)
    {
        var name = app.Name ?? string.Empty;
        var exe = app.Exe ?? string.Empty;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
            || exe.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || exe.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 0;
    }
}

public static class AppSuggestionCatalog
{
    public static IReadOnlyList<InstalledAppInfo> CommonApps { get; } = new InstalledAppInfo[]
    {
        App("Google Chrome", "chrome.exe"),
        App("Microsoft Edge", "msedge.exe"),
        App("Mozilla Firefox", "firefox.exe"),
        App("Яндекс Браузер", "browser.exe"),
        App("Opera", "opera.exe"),
        App("Word", "winword.exe"),
        App("Excel", "excel.exe"),
        App("PowerPoint", "powerpnt.exe"),
        App("Блокнот", "notepad.exe"),
        App("Калькулятор", "calc.exe"),
        App("Paint", "mspaint.exe"),
        App("Discord", "discord.exe"),
        App("Telegram", "telegram.exe"),
        App("WhatsApp", "whatsapp.exe"),
        App("Skype", "skype.exe"),
        App("Zoom", "zoom.exe"),
        App("Steam", "steam.exe"),
        App("Epic Games", "epicgameslauncher.exe"),
        App("Minecraft", "minecraft.exe"),
        App("Roblox", "robloxplayerbeta.exe"),
        App("Spotify", "spotify.exe"),
        App("VLC", "vlc.exe"),
        App("1С:Предприятие", "1cv8.exe"),
    };

    private static InstalledAppInfo App(string name, string exe) => new()
    {
        Name = name,
        Exe = exe
    };
}
