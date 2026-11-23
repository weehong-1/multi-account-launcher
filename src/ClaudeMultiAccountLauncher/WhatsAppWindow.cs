using System.Diagnostics;
using System.Runtime.InteropServices;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.WhatsApp;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace ClaudeMultiAccountLauncher;

/// <summary>
/// One WhatsApp profile: this process's only window, hosting WhatsApp Web in WebView2 with the
/// profile's own data folder (WhatsApp spec section 2, FR-W1 to FR-W9). Nothing is injected into
/// the page; the window only reads its title (FR-W3). Closing it quits the profile (FR-W4).
/// </summary>
public sealed class WhatsAppWindow : Window
{
    private readonly Profile _profile;
    private readonly LauncherPaths _paths;
    private readonly WebView2 _web = new();
    private readonly TaskbarBadge _badge;
    private readonly IntPtr _hwnd;
    private readonly FileSystemWatcher? _profilesWatcher;
    private string _name;
    private int _unread;

    public WhatsAppWindow(Profile profile, LauncherPaths paths)
    {
        _profile = profile;
        _paths = paths;
        _name = profile.Name;
        Title = WhatsAppWeb.WindowTitle(_name, _unread);
        Content = _web;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _badge = new TaskbarBadge(_hwnd);

        // The WhatsApp icon copied for shortcuts (WhatsApp spec section 4), else the launcher's own
        var icon = File.Exists(paths.WhatsAppIconFile)
            ? paths.WhatsAppIconFile
            : Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 850));

        // FR-W6: another launch of this profile handed off to us
        Program.WhatsAppActivated += OnHandedOff;
        Closed += (_, _) => Program.WhatsAppActivated -= OnHandedOff;

        // A rename in the launcher (another process) only changes profiles.json; follow it so the
        // title and taskbar button show the new name without reopening the window
        _profilesWatcher = WatchProfiles();
        Closed += (_, _) => _profilesWatcher?.Dispose();

        _ = InitAsync();
    }

    private FileSystemWatcher? WatchProfiles()
    {
        var folder = Path.GetDirectoryName(_paths.ProfilesFile);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        var file = Path.GetFileName(_paths.ProfilesFile);
        var watcher = new FileSystemWatcher(folder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        // ProfileStore.Save writes a temp file and moves it over profiles.json: a rename event
        void OnEvent(object? sender, FileSystemEventArgs e)
        {
            if (string.Equals(e.Name, file, StringComparison.OrdinalIgnoreCase))
            {
                DispatcherQueue.TryEnqueue(ReloadName);
            }
        }

        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Renamed += OnEvent;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void ReloadName()
    {
        try
        {
            var current = new ProfileStore(_paths.ProfilesFile).Load()
                .FirstOrDefault(p => p.Slug == _profile.Slug && p.App == ProfileApp.WhatsApp);
            if (current is not null && current.Name != _name)
            {
                _name = current.Name;
                Title = WhatsAppWeb.WindowTitle(_name, _unread);
            }
        }
        catch (Exception ex) when (ex is ProfileStoreException or IOException or UnauthorizedAccessException)
        {
            // Mid-write or unreadable: keep the current name; the next change event tries again
        }
    }

    private void OnHandedOff() => DispatcherQueue.TryEnqueue(BringToFront);

    /// <summary>Restores and focuses the window, as a second Launch of the profile should.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        SetForegroundWindow(_hwnd);
    }

    private async Task InitAsync()
    {
        try
        {
            var dataFolder = _paths.WhatsAppDataFolder(_profile.Slug);
            Directory.CreateDirectory(dataFolder);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, dataFolder, new CoreWebView2EnvironmentOptions());
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            ShowProblem($"WhatsApp Web couldn't start in this window: {ex.Message}");
            return;
        }

        var core = _web.CoreWebView2;
        core.DocumentTitleChanged += (_, _) => UpdateUnread(core.DocumentTitle);
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;

        // FR-W7: permission requests (microphone, camera, clipboard...) keep WebView2's own prompt.
        // FR-W9: downloads keep WebView2's default, the Windows Downloads folder.
        core.Navigate(WhatsAppWeb.Home.AbsoluteUri);
    }

    private void UpdateUnread(string title)
    {
        _unread = WhatsAppWeb.UnreadCount(title);
        Title = WhatsAppWeb.WindowTitle(_name, _unread);
        _badge.Show(_unread);
    }

    // FR-W5: the window itself only ever shows WhatsApp Web
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && WhatsAppWeb.StaysInWindow(uri))
        {
            return;
        }

        e.Cancel = true;
        OpenInBrowser(e.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // never a second WebView2 window
        OpenInBrowser(e.Uri);
    }

    private static void OpenInBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !WhatsAppWeb.OpensInBrowser(uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No default browser set; nothing sensible to do from here.
        }
    }

    private void ShowProblem(string message) =>
        Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
            IsTextSelectionEnabled = true,
        };

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
