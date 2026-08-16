using System.Collections.Generic;
using NordControl.Core.Policies;
using NordControl.Protocol;
using Xunit;

namespace NordControl.Tests;

public class InstalledAppsScannerTests
{
    [Fact]
    public void ScanInstalledApps_returns_list_without_throwing()
    {
        var scanner = new InstalledAppsScanner();
        var apps = scanner.ScanInstalledApps();

        Assert.NotNull(apps);
        // Even in environments without many apps, shouldn't throw.
        // Each returned item should have non-empty Name and Exe
        foreach (var app in apps)
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.False(string.IsNullOrWhiteSpace(app.Exe));
            Assert.EndsWith(".exe", app.Exe, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FilterAndDeduplicate_filters_uninstallers_and_deduplicates()
    {
        var rawList = new List<InstalledAppInfo>
        {
            new() { Name = "Google Chrome", Exe = "chrome.exe", LaunchTarget = @"C:\Path\chrome.exe" },
            new() { Name = "Google Chrome", Exe = "chrome.exe", LaunchTarget = @"C:\Path2\chrome.exe" },
            new() { Name = "Uninstall Chrome", Exe = "uninstall.exe", LaunchTarget = @"C:\Path\uninstall.exe" },
            new() { Name = "Unins000 App", Exe = "unins000.exe", LaunchTarget = @"C:\Path\unins000.exe" },
            new() { Name = "Visual Studio Code", Exe = "Code.exe", LaunchTarget = @"C:\VSCode\Code.exe" }
        };

        var filtered = InstalledAppsScanner.FilterAndDeduplicate(rawList);

        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, a => a.Exe.Equals("chrome.exe", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(filtered, a => a.Exe.Equals("Code.exe", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(filtered, a => a.Exe.StartsWith("unins", System.StringComparison.OrdinalIgnoreCase));
    }
}
