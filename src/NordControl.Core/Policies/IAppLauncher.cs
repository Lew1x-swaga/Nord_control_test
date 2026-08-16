namespace NordControl.Core.Policies;

public interface IAppLauncher
{
    bool Launch(string exe, string? launchTarget = null);
}
