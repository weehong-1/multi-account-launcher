using System.Diagnostics;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Startup;

namespace ClaudeMultiAccountLauncher.Core.WhatsApp;

/// <summary>
/// Starts WhatsApp: the official app for the WhatsApp Default, or a launcher process hosting
/// WhatsApp Web for a profile (WhatsApp spec section 2).
/// </summary>
/// <param name="launcherExePath">This launcher's exe; each WhatsApp profile runs as its own process of it.</param>
public sealed class WhatsAppLauncher(LauncherPaths paths, string launcherExePath)
{
    /// <summary>Starts the official WhatsApp Desktop. If it is running, it comes to the front itself.</summary>
    public void LaunchDefault() => Process.Start(DefaultStartInfo())?.Dispose();

    /// <summary>Starts a profile's process. Creates its data folder first.</summary>
    public Process? LaunchProfile(Profile profile)
    {
        EnsureFolder(profile.Slug);
        return Process.Start(ProfileStartInfo(profile));
    }

    public void EnsureFolder(string slug) => Directory.CreateDirectory(paths.WhatsAppDataFolder(slug));

    public static ProcessStartInfo DefaultStartInfo() => new(WhatsAppPackage.ShellTarget) { UseShellExecute = true };

    public ProcessStartInfo ProfileStartInfo(Profile profile)
    {
        if (profile.App != ProfileApp.WhatsApp)
        {
            throw new ArgumentException($"\"{profile.Slug}\" is not a WhatsApp profile.", nameof(profile));
        }

        var psi = new ProcessStartInfo(launcherExePath)
        {
            UseShellExecute = false,
            WorkingDirectory = paths.UserProfile,
        };
        psi.ArgumentList.Add(StartupCommand.WhatsAppSwitch);
        psi.ArgumentList.Add(profile.Slug);
        return psi;
    }
}
