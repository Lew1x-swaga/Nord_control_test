using System;
using System.Collections.Generic;

namespace NordControl.Core.Helpers;

public static class ProcessListFilter
{
    private static readonly HashSet<string> NoiseExe = new(StringComparer.OrdinalIgnoreCase)
    {
        "textinputhost.exe",
        "searchhost.exe",
        "shellexperiencehost.exe",
        "applicationframehost.exe",
        "dwm.exe",
        "svchost.exe",
        "nordcontrol.student.exe",
        "nordcontrol.teacher.exe"
    };

    public static bool IsNoiseExe(string? exe)
    {
        var normalized = ProcessNameHelper.Normalize(exe);
        if (string.IsNullOrEmpty(normalized))
        {
            return true;
        }

        return NoiseExe.Contains(normalized);
    }
}
