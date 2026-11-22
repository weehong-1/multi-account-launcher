using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Startup;
using ClaudeMultiAccountLauncher.Core.WhatsApp;
using Microsoft.UI.Xaml;

namespace ClaudeMultiAccountLauncher;

/// <summary>What the window should do once it opens, when started from a shortcut.</summary>
public abstract record StartupAction
{
    public sealed record ShowError(string Message) : StartupAction;
}

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var command = StartupCommand.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
        StartupAction? action = command.Error is { } error ? new StartupAction.ShowError(error) : null;

        // WhatsApp spec section 2: "--whatsapp <slug>" makes this process that profile's window.
        // Program.Main already handed off if the profile was running (FR-W6).
        if (command.WhatsAppSlug is { } whatsAppSlug)
        {
            switch (OpenWhatsApp(whatsAppSlug))
            {
                case null:
                    return;
                case { } problem:
                    action = new StartupAction.ShowError(problem);
                    break;
            }
        }

        // FR-8: a shortcut runs "--launch <slug>". Start the profile's app and quit without a
        // window, unless something went wrong.
        if (command.LaunchSlug is { } slug)
        {
            var paths = LauncherPaths.ForCurrentUser();
            var outcome = new QuickLauncher(
                new ProfileStore(paths.ProfilesFile),
                new ClaudeLocator(paths, new SystemInstallProbe()),
                new WhatsAppLocator(new SystemWhatsAppProbe()),
                new SystemAppStarter(new ClaudeLauncher(paths), new WhatsAppLauncher(paths, Environment.ProcessPath!)))
                .Run(slug);

            switch (outcome)
            {
                case QuickLaunchOutcome.Launched:
                    Exit();
                    return;
                case QuickLaunchOutcome.Failed failed:
                    action = new StartupAction.ShowError(failed.Message);
                    break;
            }
        }

        _window = new MainWindow(action);
        _window.Activate();
    }

    /// <summary>The address given to users when WhatsApp needs a WebView2 runtime (no automatic download).</summary>
    public const string WebView2DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>Opens the profile's WhatsApp window. Returns null on success, or what went wrong.</summary>
    private string? OpenWhatsApp(string slug)
    {
        Profile? profile;
        var paths = LauncherPaths.ForCurrentUser();
        try
        {
            profile = new ProfileStore(paths.ProfilesFile).Load().FirstOrDefault(p => p.Slug == slug);
        }
        catch (ProfileStoreException ex)
        {
            return ex.Message;
        }

        if (profile is not { App: ProfileApp.WhatsApp })
        {
            return $"There is no WhatsApp profile \"{slug}\".";
        }

        // WhatsApp spec section 2: use the Evergreen runtime on the system; never download it.
        string? version;
        try
        {
            version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            version = null;
        }

        if (string.IsNullOrEmpty(version))
        {
            return "WhatsApp windows need the Microsoft Edge WebView2 Runtime, which isn't installed on this PC. "
                + $"Install the Evergreen runtime from {WebView2DownloadPage}, then open the profile again.";
        }

        _window = new WhatsAppWindow(profile, paths);
        _window.Activate();
        return null;
    }
}
