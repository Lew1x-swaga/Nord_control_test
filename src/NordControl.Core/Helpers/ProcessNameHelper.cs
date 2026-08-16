using System;
using System.IO;

namespace NordControl.Core.Helpers;

public static class ProcessNameHelper
{
    public const string ExeExtension = ".exe";

    public static string Normalize(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var trimmed = rawName.Trim();
        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = trimmed;
        }

        var normalized = fileName.ToLowerInvariant();
        if (!normalized.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase))
        {
            normalized += ExeExtension;
        }

        return normalized;
    }

    public static bool IsExe(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Trim().EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Equals(string? name1, string? name2)
    {
        return string.Equals(Normalize(name1), Normalize(name2), StringComparison.OrdinalIgnoreCase);
    }
}
