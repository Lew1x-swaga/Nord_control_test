using System;
using System.IO;
using NordControl.Core.Logging;
using Xunit;

namespace NordControl.Tests;

public class LocalFileLoggerTests : IDisposable
{
    private readonly string _testDir;

    public LocalFileLoggerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"nord_log_tests_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Logger_WritesLogMessages_ToPrimaryFile()
    {
        var logger = new LocalFileLogger("test_app", _testDir);
        logger.Info("Hello, World!");
        logger.Warning("This is a warning.");
        logger.Error("An error occurred.", new InvalidOperationException("Test exception"));

        var logPath = Path.Combine(_testDir, "test_app.log");
        Assert.True(File.Exists(logPath));

        var content = File.ReadAllText(logPath);
        Assert.Contains("[INFO] Hello, World!", content);
        Assert.Contains("[WARNING] This is a warning.", content);
        Assert.Contains("[ERROR] An error occurred. | InvalidOperationException: Test exception", content);
    }

    [Fact]
    public void Logger_RotatesFiles_WhenSizeExceedsThreshold()
    {
        // Small max size to trigger rotation quickly (200 bytes)
        var logger = new LocalFileLogger("rot_app", _testDir, maxFileSizeBytes: 200, maxArchiveFiles: 3);

        for (var i = 1; i <= 20; i++)
        {
            logger.Info($"Message payload line number {i:D3} with some extra padding to exceed size limit");
        }

        var log0 = Path.Combine(_testDir, "rot_app.log");
        var log1 = Path.Combine(_testDir, "rot_app.1.log");
        var log2 = Path.Combine(_testDir, "rot_app.2.log");
        var log3 = Path.Combine(_testDir, "rot_app.3.log");
        var log4 = Path.Combine(_testDir, "rot_app.4.log");

        Assert.True(File.Exists(log0));
        Assert.True(File.Exists(log1));
        Assert.True(File.Exists(log2));
        Assert.True(File.Exists(log3));
        Assert.False(File.Exists(log4)); // Max 3 archives, so .4 must not exist
    }

    [Fact]
    public void Logger_IsFailSafe_DoesNotThrowOnInvalidPath()
    {
        // An invalid path on Windows like a NUL device or invalid characters
        var logger = new LocalFileLogger("app", @"Z:\NonExistent_Drive_12345\Invalid_Path");

        var ex = Record.Exception(() =>
        {
            logger.Info("Test message that shouldn't crash");
        });

        Assert.Null(ex);
    }
}
