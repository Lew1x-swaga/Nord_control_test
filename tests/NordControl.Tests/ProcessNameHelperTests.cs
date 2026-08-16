using NordControl.Core.Helpers;
using Xunit;

namespace NordControl.Tests;

public class ProcessNameHelperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("notepad", "notepad.exe")]
    [InlineData("notepad.exe", "notepad.exe")]
    [InlineData("NOTEPAD.EXE", "notepad.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad.exe")]
    [InlineData(@"D:/Apps/Game/game.EXE", "game.exe")]
    [InlineData("  discord.exe  ", "discord.exe")]
    [InlineData("steam", "steam.exe")]
    public void Normalize_HandlesVariousInputs(string? input, string expected)
    {
        var result = ProcessNameHelper.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("notepad", false)]
    [InlineData("notepad.exe", true)]
    [InlineData("NOTEPAD.EXE", true)]
    [InlineData("notepad.dll", false)]
    public void IsExe_ValidatesCorrectly(string? input, bool expected)
    {
        var result = ProcessNameHelper.IsExe(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("notepad", "NOTEPAD.exe", true)]
    [InlineData(@"C:\Windows\System32\notepad.exe", "notepad", true)]
    [InlineData("calc.exe", "notepad.exe", false)]
    [InlineData(null, "", true)]
    public void Equals_ComparesNormalizedNames(string? a, string? b, bool expected)
    {
        var result = ProcessNameHelper.Equals(a, b);
        Assert.Equal(expected, result);
    }
}
