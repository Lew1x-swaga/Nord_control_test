using System;
using System.IO;
using System.Text.Json;
using NordControl.Core;
using Xunit;

namespace NordControl.Tests;

public class TeacherUiSettingsTests
{
    [Fact]
    public void DefaultLayout_IsList()
    {
        var settings = new TeacherUiSettings();
        Assert.Equal(StudentListLayout.List, settings.Layout);
    }

    [Fact]
    public void Load_MissingFile_ReturnsListWithoutThrowing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"teacher_ui_missing_{Guid.NewGuid():N}.json");
        try
        {
            var settings = TeacherUiSettingsManager.Load(tempFile);
            Assert.Equal(StudentListLayout.List, settings.Layout);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Load_CorruptJson_ReturnsListWithoutThrowing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"teacher_ui_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, "{ corrupted json invalid syntax ... ");
            var settings = TeacherUiSettingsManager.Load(tempFile);
            Assert.Equal(StudentListLayout.List, settings.Layout);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsGrid()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"teacher_ui_roundtrip_{Guid.NewGuid():N}.json");
        try
        {
            var original = new TeacherUiSettings { Layout = StudentListLayout.Grid };
            TeacherUiSettingsManager.Save(original, tempFile);
            Assert.True(File.Exists(tempFile));

            var loaded = TeacherUiSettingsManager.Load(tempFile);
            Assert.Equal(StudentListLayout.Grid, loaded.Layout);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_UsesSnakeCaseStudentListLayoutKey()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"teacher_ui_snake_{Guid.NewGuid():N}.json");
        try
        {
            TeacherUiSettingsManager.Save(new TeacherUiSettings { Layout = StudentListLayout.Grid }, tempFile);
            var json = File.ReadAllText(tempFile);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("student_list_layout", out var layoutProp));
            Assert.Equal("grid", layoutProp.GetString());
            Assert.False(doc.RootElement.TryGetProperty("quick_apps", out _));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Save_CreatesNordControlDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"nord_ui_dir_{Guid.NewGuid():N}");
        var nestedPath = Path.Combine(tempRoot, "NordControl", "teacher-ui.json");
        try
        {
            Assert.False(Directory.Exists(Path.GetDirectoryName(nestedPath)!));
            TeacherUiSettingsManager.Save(new TeacherUiSettings { Layout = StudentListLayout.List }, nestedPath);
            Assert.True(Directory.Exists(Path.Combine(tempRoot, "NordControl")));
            Assert.True(File.Exists(nestedPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultFilePath_EndsWithTeacherUiJsonUnderNordControl()
    {
        var path = TeacherUiSettingsManager.GetDefaultFilePath();
        Assert.EndsWith(Path.Combine("NordControl", "teacher-ui.json"), path, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 168, 1)]
    [InlineData(-10, 168, 1)]
    [InlineData(500, 168, 2)]
    [InlineData(168, 168, 1)]
    [InlineData(167, 168, 1)]
    [InlineData(336, 168, 2)]
    public void ColumnCount_ReturnsExpected(double panelWidth, int minCardWidth, int expected)
    {
        Assert.Equal(expected, StudentGridLayout.ColumnCount(panelWidth, minCardWidth));
    }

    [Fact]
    public void ColumnCount_DefaultMinCardWidth_Is168()
    {
        Assert.Equal(2, StudentGridLayout.ColumnCount(500));
    }
}
