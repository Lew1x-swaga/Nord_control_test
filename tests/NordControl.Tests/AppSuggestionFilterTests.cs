using NordControl.Core.Policies;
using NordControl.Protocol;

namespace NordControl.Tests;

public class AppSuggestionFilterTests
{
    private static readonly InstalledAppInfo Chrome = new()
    {
        Name = "Google Chrome",
        Exe = "chrome.exe",
        LaunchTarget = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
    };

    private static readonly InstalledAppInfo Discord = new()
    {
        Name = "Discord",
        Exe = "discord.exe"
    };

    private static readonly InstalledAppInfo Edge = new()
    {
        Name = "Microsoft Edge",
        Exe = "msedge.exe"
    };

    [Fact]
    public void Filter_EmptyQuery_ReturnsPreferredFirstUpToLimit()
    {
        var catalog = AppSuggestionFilter.Merge(new[] { Discord }, new[] { Chrome, Edge });
        var result = AppSuggestionFilter.Filter(catalog, "", maxResults: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("discord.exe", result[0].Exe);
        Assert.Equal("chrome.exe", result[1].Exe);
    }

    [Fact]
    public void Filter_MatchesNameOrExe()
    {
        var catalog = new[] { Chrome, Discord, Edge };

        var byName = AppSuggestionFilter.Filter(catalog, "chro");
        Assert.Contains(byName, a => a.Exe == "chrome.exe");

        var byExe = AppSuggestionFilter.Filter(catalog, "discord.exe");
        Assert.Single(byExe);
        Assert.Equal("Discord", byExe[0].Name);
    }

    [Fact]
    public void Merge_DedupesByExe_KeepsPreferred()
    {
        var preferred = new[]
        {
            new InstalledAppInfo { Name = "Chrome с ПК ученика", Exe = "chrome.exe", LaunchTarget = @"D:\Chrome\chrome.exe" }
        };
        var fallback = new[] { Chrome, Discord };

        var merged = AppSuggestionFilter.Merge(preferred, fallback);
        Assert.Equal(2, merged.Count);
        Assert.Equal("Chrome с ПК ученика", merged[0].Name);
        Assert.Equal(@"D:\Chrome\chrome.exe", merged[0].LaunchTarget);
        Assert.Equal("discord.exe", merged[1].Exe);
    }

    [Fact]
    public void CommonApps_ContainsTypicalClassroomTargets()
    {
        Assert.Contains(AppSuggestionCatalog.CommonApps, a => a.Exe == "chrome.exe");
        Assert.Contains(AppSuggestionCatalog.CommonApps, a => a.Exe == "discord.exe");
        Assert.Contains(AppSuggestionCatalog.CommonApps, a => a.Exe == "winword.exe");
    }
}
