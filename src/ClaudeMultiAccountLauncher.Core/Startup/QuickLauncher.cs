using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Startup;

public abstract record QuickLaunchOutcome
{
    /// <summary>The app was started; the launcher can exit without showing a window.</summary>
    public sealed record Launched : QuickLaunchOutcome;

    /// <summary>Show the window with this error.</summary>
    public sealed record Failed(string Message) : QuickLaunchOutcome;
}

/// <summary>What a quick launch starts. Seam so tests never start real processes.</summary>
public interface IAppStarter
{
    /// <param name="profile">Null means the built-in Claude Default.</param>
    void StartClaude(ClaudeInstall install, Profile? profile);

    void StartWhatsAppDefault(WhatsAppDesktop desktop);

    void StartWhatsAppProfile(Profile profile);
}

/// <summary>Starts real processes through <see cref="ClaudeLauncher"/> and <see cref="WhatsAppLauncher"/>.</summary>
public sealed class SystemAppStarter(ClaudeLauncher claude, WhatsAppLauncher whatsApp) : IAppStarter
{
    public void StartClaude(ClaudeInstall install, Profile? profile)
    {
        using var _ = claude.Launch(install, profile);
    }

    public void StartWhatsAppDefault(WhatsAppDesktop desktop) => whatsApp.LaunchDefault();

    public void StartWhatsAppProfile(Profile profile)
    {
        using var _ = whatsApp.LaunchProfile(profile);
    }
}

/// <summary>
/// Handles <c>--launch &lt;slug&gt;</c> from a desktop shortcut (FR-8): one click opens the
/// profile, and the launcher window only appears when something went wrong. The slug picks the
/// app: <c>default</c> is Claude's Default, <c>whatsapp-default</c> the official WhatsApp, and any
/// other slug the profile's own app (WhatsApp spec, decision 3.5).
/// </summary>
public sealed class QuickLauncher(ProfileStore store, ClaudeLocator claudeLocator, WhatsAppLocator whatsAppLocator, IAppStarter starter)
{
    public QuickLaunchOutcome Run(string slug)
    {
        try
        {
            if (slug == StartupCommand.DefaultSlug)
            {
                return StartClaude(null);
            }

            if (slug == StartupCommand.WhatsAppDefaultSlug)
            {
                return StartWhatsAppDefault();
            }

            Profile? profile;
            try
            {
                profile = store.Load().FirstOrDefault(p => p.Slug == slug);
            }
            catch (ProfileStoreException ex)
            {
                return new QuickLaunchOutcome.Failed(ex.Message);
            }

            if (profile is null)
            {
                return new QuickLaunchOutcome.Failed(
                    $"The shortcut points to a profile that no longer exists (\"{slug}\"). Delete the shortcut or create it again.");
            }

            if (profile.App == ProfileApp.WhatsApp)
            {
                starter.StartWhatsAppProfile(profile);
                return new QuickLaunchOutcome.Launched();
            }

            return StartClaude(profile);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new QuickLaunchOutcome.Failed($"Couldn't start it: {ex.Message}");
        }
    }

    private QuickLaunchOutcome StartClaude(Profile? profile)
    {
        ClaudeInstall install;
        try
        {
            install = claudeLocator.Locate();
        }
        catch (ClaudeNotFoundException ex)
        {
            return new QuickLaunchOutcome.Failed(ex.Message);
        }

        try
        {
            starter.StartClaude(install, profile);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new QuickLaunchOutcome.Failed($"Couldn't start Claude: {ex.Message}");
        }

        return new QuickLaunchOutcome.Launched();
    }

    private QuickLaunchOutcome StartWhatsAppDefault()
    {
        if (whatsAppLocator.Locate() is not { } desktop)
        {
            return new QuickLaunchOutcome.Failed(
                "WhatsApp Desktop isn't installed, so the WhatsApp Default can't open. Install it from the Microsoft Store, or delete this shortcut.");
        }

        try
        {
            starter.StartWhatsAppDefault(desktop);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new QuickLaunchOutcome.Failed($"Couldn't start WhatsApp: {ex.Message}");
        }

        return new QuickLaunchOutcome.Launched();
    }
}
