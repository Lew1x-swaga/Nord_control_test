using System;
using System.IO;
using System.Text;

namespace NordControl.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public class LocalFileLogger
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const int DefaultMaxArchiveFiles = 3;

    private readonly string _logDirectory;
    private readonly string _logName;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxArchiveFiles;
    private readonly object _lock = new();

    public string LogDirectory => _logDirectory;
    public string LogName => _logName;
    public long MaxFileSizeBytes => _maxFileSizeBytes;
    public int MaxArchiveFiles => _maxArchiveFiles;

    public static string GetDefaultLogDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "NordControl", "logs");
    }

    public LocalFileLogger(
        string logName = "app",
        string? logDirectory = null,
        long maxFileSizeBytes = DefaultMaxFileSizeBytes,
        int maxArchiveFiles = DefaultMaxArchiveFiles)
    {
        _logName = string.IsNullOrWhiteSpace(logName) ? "app" : logName.Trim();
        _logDirectory = logDirectory ?? GetDefaultLogDirectory();
        _maxFileSizeBytes = maxFileSizeBytes > 0 ? maxFileSizeBytes : DefaultMaxFileSizeBytes;
        _maxArchiveFiles = Math.Max(1, maxArchiveFiles);
    }

    public void Info(string message) => Log(LogLevel.Info, message);

    public void Warning(string message) => Log(LogLevel.Warning, message);

    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = exception != null
                ? $"[{timestamp}] [{level.ToString().ToUpperInvariant()}] {message} | {exception.GetType().Name}: {exception.Message}"
                : $"[{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";

            lock (_lock)
            {
                EnsureDirectoryExists();
                var primaryFilePath = Path.Combine(_logDirectory, $"{_logName}.log");

                if (File.Exists(primaryFilePath))
                {
                    var fileInfo = new FileInfo(primaryFilePath);
                    if (fileInfo.Length >= _maxFileSizeBytes)
                    {
                        RotateFiles(primaryFilePath);
                    }
                }

                File.AppendAllText(primaryFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Fail-safe: Logging must never crash the application or throw exceptions
        }
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    private void RotateFiles(string primaryFilePath)
    {
        try
        {
            // Delete oldest archive if it exists
            var oldestArchive = Path.Combine(_logDirectory, $"{_logName}.{_maxArchiveFiles}.log");
            if (File.Exists(oldestArchive))
            {
                File.Delete(oldestArchive);
            }

            // Shift existing archives (e.g., .2 -> .3, .1 -> .2)
            for (var i = _maxArchiveFiles - 1; i >= 1; i--)
            {
                var currentArchive = Path.Combine(_logDirectory, $"{_logName}.{i}.log");
                var nextArchive = Path.Combine(_logDirectory, $"{_logName}.{i + 1}.log");

                if (File.Exists(currentArchive))
                {
                    if (File.Exists(nextArchive))
                    {
                        File.Delete(nextArchive);
                    }
                    File.Move(currentArchive, nextArchive);
                }
            }

            // Move primary log to .1.log
            var firstArchive = Path.Combine(_logDirectory, $"{_logName}.1.log");
            if (File.Exists(firstArchive))
            {
                File.Delete(firstArchive);
            }

            File.Move(primaryFilePath, firstArchive);
        }
        catch
        {
            // Suppress rotation errors
        }
    }
}
