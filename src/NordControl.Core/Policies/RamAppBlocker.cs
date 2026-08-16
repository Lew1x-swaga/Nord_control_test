using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NordControl.Core.Policies;

public record ProcessCandidate(int Pid, string ExeName, Action KillAction);

public class RamAppBlocker : IAppBlocker
{
    private readonly HashSet<string> _blockList = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly HashSet<string> ProtectedExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Teacher.exe",
        "Student.exe",
        "dotnet.exe",
        "testhost.exe",
        "devenv.exe",
        "msbuild.exe",
        "explorer.exe",
        "dwm.exe",
        "csrss.exe",
        "lsass.exe",
        "smss.exe",
        "winlogon.exe",
        "services.exe"
    };

    private const string ProtectedPrefix = "NordControl";

    private readonly Func<IEnumerable<ProcessCandidate>> _processEnumerator;
    private readonly int _checkIntervalMs;
    private readonly CancellationTokenSource? _cts;
    private readonly Task? _watcherTask;
    private bool _isDisposed;

    public RamAppBlocker(
        Func<IEnumerable<ProcessCandidate>>? processEnumerator = null,
        int checkIntervalMs = 500,
        bool autoStartWatcher = true)
    {
        _processEnumerator = processEnumerator ?? DefaultProcessEnumerator;
        _checkIntervalMs = checkIntervalMs;

        if (autoStartWatcher)
        {
            _cts = new CancellationTokenSource();
            _watcherTask = Task.Run(() => WatcherLoopAsync(_cts.Token));
        }
    }

    public void SetBlockList(IEnumerable<string> exeNames)
    {
        lock (_lock)
        {
            _blockList.Clear();
            if (exeNames != null)
            {
                foreach (var item in exeNames)
                {
                    var normalized = NormalizeExeName(item);
                    if (!string.IsNullOrEmpty(normalized) && !IsProtected(normalized))
                    {
                        _blockList.Add(normalized);
                    }
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _blockList.Clear();
        }
    }

    public IReadOnlyCollection<string> GetBlockList()
    {
        lock (_lock)
        {
            return _blockList.ToList();
        }
    }

    public bool IsBlocked(string exeName)
    {
        var normalized = NormalizeExeName(exeName);
        if (string.IsNullOrEmpty(normalized) || IsProtected(normalized))
        {
            return false;
        }

        lock (_lock)
        {
            return _blockList.Contains(normalized);
        }
    }

    public void CheckAndEnforce()
    {
        lock (_lock)
        {
            if (_blockList.Count == 0)
            {
                return;
            }
        }

        try
        {
            var candidates = _processEnumerator();
            foreach (var candidate in candidates)
            {
                var normalized = NormalizeExeName(candidate.ExeName);
                if (string.IsNullOrEmpty(normalized) || IsProtected(normalized))
                {
                    continue;
                }

                bool shouldKill;
                lock (_lock)
                {
                    shouldKill = _blockList.Contains(normalized);
                }

                if (shouldKill)
                {
                    try
                    {
                        candidate.KillAction();
                    }
                    catch
                    {
                        // Ignore process termination errors (e.g. access denied or already exited)
                    }
                }
            }
        }
        catch
        {
            // Ignore enumeration errors
        }
    }

    private async Task WatcherLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                CheckAndEnforce();
                await Task.Delay(_checkIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Never crash the background loop
            }
        }
    }

    public static bool IsProtected(string exeName)
    {
        var normalized = NormalizeExeName(exeName);
        if (string.IsNullOrEmpty(normalized))
            return true;

        if (normalized.StartsWith(ProtectedPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (ProtectedExeNames.Contains(normalized))
            return true;

        return false;
    }

    public static string NormalizeExeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var name = Path.GetFileName(raw.Trim()).ToLowerInvariant();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }
        return name;
    }

    private static IEnumerable<ProcessCandidate> DefaultProcessEnumerator()
    {
        var result = new List<ProcessCandidate>();
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        foreach (var p in processes)
        {
            try
            {
                string exeName;
                try
                {
                    exeName = Path.GetFileName(p.MainModule?.FileName ?? p.ProcessName + ".exe");
                }
                catch
                {
                    exeName = p.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? p.ProcessName
                        : p.ProcessName + ".exe";
                }

                result.Add(new ProcessCandidate(
                    Pid: p.Id,
                    ExeName: exeName,
                    KillAction: () =>
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch { }
                    }
                ));
            }
            catch
            {
                // Process could have exited
            }
            finally
            {
                p.Dispose();
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Clear();

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
