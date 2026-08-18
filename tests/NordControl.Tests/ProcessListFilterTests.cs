using NordControl.Core.Helpers;
using Xunit;

namespace NordControl.Tests;

public class ProcessListFilterTests
{
    [Theory]
    [InlineData("notepad.exe", false)]
    [InlineData("mspaint.exe", false)]
    [InlineData("chrome.exe", false)]
    [InlineData("calculatorapp.exe", false)]
    [InlineData("textinputhost.exe", true)]
    [InlineData("TextInputHost.exe", true)]
    [InlineData("searchhost.exe", true)]
    [InlineData("shellexperiencehost.exe", true)]
    [InlineData("applicationframehost.exe", true)]
    [InlineData("dwm.exe", true)]
    [InlineData("svchost.exe", true)]
    [InlineData("NordControl.Student.exe", true)]
    [InlineData("NordControl.Teacher.exe", true)]
    public void IsNoiseExe_FiltersHostsImeAndSelf(string exe, bool noise)
    {
        Assert.Equal(noise, ProcessListFilter.IsNoiseExe(exe));
    }
}
