using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NordControl.Protocol;

namespace NordControl.Core.Policies;

public class TeacherPreset
{
    public List<InstalledAppInfo> QuickApps { get; set; } = new();
    public List<string> BlockedApps { get; set; } = new();
}

public static class TeacherPresetManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string GetDefaultFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "NordControl", "teacher-preset.json");
    }

    public static TeacherPreset Load(string? customPath = null)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            if (!File.Exists(path))
            {
                return new TeacherPreset();
            }

            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<TeacherPreset>(json, JsonOptions);
            return result ?? new TeacherPreset();
        }
        catch
        {
            return new TeacherPreset();
        }
    }

    public static async Task<TeacherPreset> LoadAsync(string? customPath = null, CancellationToken ct = default)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            if (!File.Exists(path))
            {
                return new TeacherPreset();
            }

            var json = await File.ReadAllTextAsync(path, ct);
            var result = JsonSerializer.Deserialize<TeacherPreset>(json, JsonOptions);
            return result ?? new TeacherPreset();
        }
        catch
        {
            return new TeacherPreset();
        }
    }

    public static void Save(TeacherPreset preset, string? customPath = null)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(preset, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal, suppress file write errors
        }
    }

    public static async Task SaveAsync(TeacherPreset preset, string? customPath = null, CancellationToken ct = default)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(preset, JsonOptions);
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch
        {
            // Non-fatal, suppress file write errors
        }
    }
}
