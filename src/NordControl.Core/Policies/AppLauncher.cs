using System;
using System.Diagnostics;

namespace NordControl.Core.Policies;

public class AppLauncher : IAppLauncher
{
    private readonly Func<string, bool>? _startAction;

    public AppLauncher(Func<string, bool>? startAction = null)
    {
        _startAction = startAction;
    }

    public bool Launch(string exe, string? launchTarget = null)
    {
        try
        {
            var target = !string.IsNullOrWhiteSpace(launchTarget)
                ? launchTarget.Trim()
                : (!string.IsNullOrWhiteSpace(exe) ? exe.Trim() : null);

            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (_startAction != null)
            {
                return _startAction(target);
            }

            var psi = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };

            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            return false;
        }
    }
}
