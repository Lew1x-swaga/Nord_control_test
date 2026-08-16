using System;
using System.Collections.Generic;
using NordControl.Core.Policies;
using Xunit;

namespace NordControl.Tests;

public class AppLauncherTests
{
    [Fact]
    public void Launch_uses_launchTarget_when_provided()
    {
        string? executedTarget = null;
        var launcher = new AppLauncher(startAction: target =>
        {
            executedTarget = target;
            return true;
        });

        var result = launcher.Launch("chrome.exe", @"C:\Program Files\Google\Chrome\chrome.exe");

        Assert.True(result);
        Assert.Equal(@"C:\Program Files\Google\Chrome\chrome.exe", executedTarget);
    }

    [Fact]
    public void Launch_uses_exe_when_launchTarget_is_null_or_whitespace()
    {
        string? executedTarget = null;
        var launcher = new AppLauncher(startAction: target =>
        {
            executedTarget = target;
            return true;
        });

        var result1 = launcher.Launch("calc.exe", null);
        Assert.True(result1);
        Assert.Equal("calc.exe", executedTarget);

        var result2 = launcher.Launch("notepad.exe", "   ");
        Assert.True(result2);
        Assert.Equal("notepad.exe", executedTarget);
    }

    [Fact]
    public void Launch_returns_false_and_does_not_throw_on_exception()
    {
        var launcher = new AppLauncher(startAction: _ => throw new InvalidOperationException("Failed to start process"));

        var result = launcher.Launch("bad_app.exe");
        Assert.False(result);
    }

    [Fact]
    public void Launch_returns_false_for_empty_target()
    {
        var launcher = new AppLauncher();
        var result = launcher.Launch("   ", "");
        Assert.False(result);
    }
}
