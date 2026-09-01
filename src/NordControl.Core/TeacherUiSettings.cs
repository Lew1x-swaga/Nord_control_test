using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NordControl.Core;

public enum StudentListLayout
{
    List,
    Grid
}

public class TeacherUiSettings
{
    [JsonPropertyName("student_list_layout")]
    public StudentListLayout Layout { get; set; } = StudentListLayout.List;
}

public static class TeacherUiSettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static string GetDefaultFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "NordControl", "teacher-ui.json");
    }

    public static TeacherUiSettings Load(string? customPath = null)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            if (!File.Exists(path))
            {
                return new TeacherUiSettings();
            }

            var json = File.ReadAllText(path);
            var result = JsonSerializer.Deserialize<TeacherUiSettings>(json, JsonOptions);
            return result ?? new TeacherUiSettings();
        }
        catch
        {
            return new TeacherUiSettings();
        }
    }

    public static void Save(TeacherUiSettings settings, string? customPath = null)
    {
        var path = customPath ?? GetDefaultFilePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-fatal, suppress file write errors
        }
    }
}

public static class StudentGridLayout
{
    public static int ColumnCount(double panelWidth, int minCardWidth = 168)
    {
        if (panelWidth <= 0 || minCardWidth <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Floor(panelWidth / minCardWidth));
    }
}
