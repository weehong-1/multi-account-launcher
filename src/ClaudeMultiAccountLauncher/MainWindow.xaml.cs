using System.Collections.ObjectModel;
using System.ComponentModel;
using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Shortcuts;
using ClaudeMultiAccountLauncher.Core.Startup;
using ClaudeMultiAccountLauncher.Core.Uninstall;
using ClaudeMultiAccountLauncher.Core.WhatsApp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace ClaudeMultiAccountLauncher;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 560;
    private const int DefaultHeight = 640;

    private readonly LauncherPaths _paths = LauncherPaths.ForCurrentUser();
    private readonly ProfileService _profiles;
    private readonly ClaudeLauncher _launcher;
    private readonly WhatsAppLauncher _whatsApp;
    private readonly ProcessScanner _scanner = new(new WmiProcessSource());

    // WhatsApp spec section 3: two sections, Claude and WhatsApp, each with its Default first
    private readonly ObservableCollection<ProfileRow> _claudeRows = [];
    private readonly ObservableCollection<ProfileRow> _whatsAppRows = [];

    /// <summary>This exe's file name; WhatsApp profiles run as processes of it (WhatsApp spec section 2).</summary>
    private readonly string _launcherImageName = Path.GetFileName(Environment.ProcessPath ?? "claude-multi-account-launcher.exe");

    private RunningState _running = RunningState.Empty;
    private bool _whatsAppChecked;

    private readonly ShortcutService _shortcuts = new(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.ProcessPath ?? string.Empty);

    private readonly ProcessExitWatcher _exitWatcher = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _poll;

    private ClaudeInstall? _install;
    private string? _installError;
    private string? _storeError;
    private bool _refreshing;
    private bool _refreshAgain;
    private StartupAction? _pendingStartup;

    public MainWindow(StartupAction? startup = null)
    {
        InitializeComponent();

        _profiles = new ProfileService(new ProfileStore(_paths.ProfilesFile), _paths);
        _launcher = new ClaudeLauncher(_paths);
        _whatsApp = new WhatsAppLauncher(_paths, Environment.ProcessPath ?? string.Empty);
        _pendingStartup = startup;
        ClaudeList.ItemsSource = _claudeRows;
        WhatsAppList.ItemsSource = _whatsAppRows;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Taskbar and Alt+Tab icon. The exe also embeds it (ApplicationIcon) for Explorer.
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        // AppWindow sizes are in physical pixels; scale the logical size by the monitor DPI.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)));

        LoadProfiles();

        // FR-7, live: refresh on focus; the moment a running Claude exits; and every few
        // seconds while the window is on screen, to catch Claude started elsewhere.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                _ = OnActivatedAsync();
            }
        };

        _exitWatcher.Exited += exitedPid => DispatcherQueue.TryEnqueue(() => _ = RefreshAsync());

        _poll = DispatcherQueue.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(3);
        _poll.Tick += (_, _) =>
        {
            if (IsOnScreen())
            {
                _ = RefreshAsync();
            }
        };
        _poll.Start();

        Closed += (_, _) =>
        {
            _poll.Stop();
            _exitWatcher.Dispose();
        };
    }

    /// <summary>No polling while minimized or hidden; focus or an exit still refreshes.</summary>
    private bool IsOnScreen() =>
        AppWindow.IsVisible
        && (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter
            || presenter.State != Microsoft.UI.Windowing.OverlappedPresenterState.Minimized);

    private async Task OnActivatedAsync()
    {
        await RefreshAsync();

        // Opened from a shortcut that needs the user (FR-8): run it once, after the first refresh.
        if (_pendingStartup is not { } startup || !Root.IsLoaded)
        {
            return;
        }

        _pendingStartup = null;
        if (startup is StartupAction.ShowError error)
        {
            ShowErrors(error.Message);
        }
    }

    // ---------- Loading and refreshing ----------

    private void LoadProfiles()
    {
        try
        {
            _profiles.Load();
            _storeError = null;
        }
        catch (ProfileStoreException ex)
        {
            // Never overwrite a file we couldn't read: block changes until it's fixed.
            _storeError = ex.Message + Environment.NewLine + "Fix or move the file, then reopen the launcher.";
        }

        AddButton.IsEnabled = _storeError is null;
        SyncRows();
    }

    /// <summary>What runs right now, from the last refresh.</summary>
    /// <param name="WhatsApp">The official WhatsApp Desktop, or null when it isn't installed.</param>
    /// <param name="WhatsAppProfiles">Running WhatsApp profiles, slug to process id.</param>
    private sealed record RunningState(
        IReadOnlyList<RunningInstance> Claude,
        WhatsAppDesktop? WhatsApp,
        bool WhatsAppDefaultRunning,
        IReadOnlyDictionary<string, int> WhatsAppProfiles)
    {
        public static RunningState Empty { get; } = new([], null, false, new Dictionary<string, int>());
    }

    private IEnumerable<ProfileRow> AllRows => _claudeRows.Concat(_whatsAppRows);

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            // A Claude exited mid-refresh: run once more afterwards so it isn't missed.
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;

                // Package lookup and WMI are slow-ish; keep them off the UI thread.
                var (install, error, running) = await Task.Run(() =>
                {
                    ClaudeInstall? found = null;
                    string? notFound = null;
                    IReadOnlyList<RunningInstance> claude = [];
                    try
                    {
                        found = new ClaudeLocator(_paths, new SystemInstallProbe()).Locate();
                        claude = _scanner.RunningInstances(found);
                    }
                    catch (ClaudeNotFoundException ex)
                    {
                        notFound = ex.Message;
                    }

                    var whatsApp = new WhatsAppLocator(new SystemWhatsAppProbe()).Locate();
                    return (found, notFound, new RunningState(
                        claude,
                        whatsApp,
                        whatsApp is not null && _scanner.IsWhatsAppDefaultRunning(new SystemWindowProbe()),
                        _scanner.RunningWhatsAppProfiles(_launcherImageName)));
                });

                _install = install;
                _installError = error;
                _running = running;
                _whatsAppChecked = true;
                SyncRows();
                _exitWatcher.Watch(running.Claude.Select(r => r.ProcessId).Concat(running.WhatsAppProfiles.Values));
            }
            while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Updates both sections from the profile list and <see cref="_running"/>.</summary>
    private void SyncRows()
    {
        var running = _running;

        var claude = new List<Profile?> { null };
        claude.AddRange(_profiles.Profiles.Where(p => p.App == ProfileApp.Claude));
        SyncSection(_claudeRows, claude, ProfileApp.Claude,
            p => p is null ? "Claude's own data folder" : _paths.DataFolder(p.Slug),
            row =>
            {
                row.IsRunning = RunningProfiles.IsRunning(row.Profile, running.Claude, _paths);
                row.CanLaunch = _install is not null;
            });

        // The WhatsApp Default is listed only when WhatsApp Desktop is installed (WhatsApp spec section 2)
        var whatsApp = new List<Profile?>();
        if (running.WhatsApp is not null)
        {
            whatsApp.Add(null);
        }

        whatsApp.AddRange(_profiles.Profiles.Where(p => p.App == ProfileApp.WhatsApp));
        SyncSection(_whatsAppRows, whatsApp, ProfileApp.WhatsApp,
            p => p is null ? "WhatsApp's own data folder" : _paths.WhatsAppDataFolder(p.Slug),
            row =>
            {
                row.IsRunning = row.Profile is null ? running.WhatsAppDefaultRunning : running.WhatsAppProfiles.ContainsKey(row.Key);
                row.CanLaunch = true;
            });
        WhatsAppEmptyText.Visibility = _whatsAppChecked && _whatsAppRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowErrors();
        FooterText.Text = _install is null
            ? string.Empty
            : $"Claude Desktop ({_install.Kind}): {_install.LaunchPath}";
    }

    /// <summary>Updates one section's rows in place so focus and open menus survive a refresh.</summary>
    private static void SyncSection(
        ObservableCollection<ProfileRow> rows,
        List<Profile?> wanted,
        ProfileApp app,
        Func<Profile?, string> folderText,
        Action<ProfileRow> update)
    {
        static string KeyOf(Profile? p, ProfileApp app) =>
            p?.Slug ?? (app == ProfileApp.WhatsApp ? StartupCommand.WhatsAppDefaultSlug : StartupCommand.DefaultSlug);

        // Drop rows whose profile is gone
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!wanted.Any(p => KeyOf(p, app) == rows[i].Key))
            {
                rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            var profile = wanted[i];
            var key = KeyOf(profile, app);
            var row = rows.FirstOrDefault(r => r.Key == key);

            if (row is null)
            {
                row = new ProfileRow(profile, app, folderText(profile));
                rows.Insert(i, row);
            }
            else
            {
                var at = rows.IndexOf(row);
                if (at != i)
                {
                    rows.Move(at, i);
                }

                row.Profile = profile;
            }

            update(row);
        }
    }

    private void ShowErrors(string? actionError = null)
    {
        var errors = new[] { actionError, _storeError, _installError }.Where(e => e is not null).ToList();
        ErrorBar.Message = string.Join(Environment.NewLine + Environment.NewLine, errors);
        ErrorBar.IsOpen = errors.Count > 0;
    }

    // ---------- Launch (FR-3) ----------

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileRow row)
        {
            return;
        }

        // WhatsApp: the official app, or the profile's own window; a running one comes to the front (FR-W6)
        if (row.IsWhatsApp)
        {
            if (StartWhatsApp(row.Profile))
            {
                await RefreshSoonAsync();
            }

            return;
        }

        if (_install is null)
        {
            return;
        }

        // Opens next to any Claude already running; if this one is running, Claude brings it to the front.
        if (StartClaude(_install, row.Profile))
        {
            await RefreshSoonAsync();
        }
    }

    private bool StartClaude(ClaudeInstall install, Profile? profile)
    {
        try
        {
            using var _ = _launcher.Launch(install, profile);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            ShowErrors($"Couldn't start Claude: {ex.Message}");
            return false;
        }
    }

    /// <param name="profile">Null starts the official WhatsApp Desktop (the WhatsApp Default).</param>
    private bool StartWhatsApp(Profile? profile)
    {
        try
        {
            if (profile is null)
            {
                _whatsApp.LaunchDefault();
            }
            else
            {
                using var _ = _whatsApp.LaunchProfile(profile);
            }

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            ShowErrors($"Couldn't start WhatsApp: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// FR-W4: Quit in a running WhatsApp profile's menu. Closes its window the way its X button
    /// does, so WebView2 can finish writing the profile's data.
    /// </summary>
    private async void QuitMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileRow { Profile: { } profile }
            || !_running.WhatsAppProfiles.TryGetValue(profile.Slug, out var processId))
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            process.CloseMainWindow();
        }
        catch (ArgumentException)
        {
            // Already gone
        }

        await RefreshSoonAsync();
    }

    /// <summary>Apps take a moment to start or quit; refresh so Running shows without refocusing.</summary>
    private async Task RefreshSoonAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        await RefreshAsync();
    }

    // ---------- First sign-in (FR-4) ----------

    /// <summary>
    /// Right after adding a profile: offer to open it, with the one thing that matters.
    /// Google sign-in returns through a claude:// link, which Windows always hands to the
    /// Default Claude (verified 2026-11-19), so the profile must sign in with email instead.
    /// Nothing is closed.
    /// </summary>
    private async Task OfferFirstSignInAsync(Profile profile)
    {
        if (_install is null)
        {
            return;
        }

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Claude opens in a new window next to the one you have now. Nothing gets closed." });
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Sign in there with " });
        var bold = new Microsoft.UI.Xaml.Documents.Bold();
        bold.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Continue with email" });
        text.Inlines.Add(bold);
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = ". Don't use Continue with Google: Google sign-in always ends up in your Default Claude window, not in this profile.",
        });

        var open = await NewDialog($"Sign in to \"{profile.Name}\"", text, primary: "Open Claude", close: "Later").ShowAsync();
        if (open == ContentDialogResult.Primary && StartClaude(_install, profile))
        {
            await RefreshSoonAsync();
        }
    }

    // ---------- Add (FR-2) ----------

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (await AskForName("Add profile", "Add", initial: string.Empty, renamingSlug: null) is not var (name, app))
        {
            return;
        }

        Profile added;
        try
        {
            added = _profiles.Add(name, app);
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            ShowErrors($"Couldn't add the profile: {ex.Message}");
            return;
        }

        await RefreshAsync();
        if (app == ProfileApp.WhatsApp)
        {
            await OfferWhatsAppSignInAsync(added);
        }
        else
        {
            await OfferFirstSignInAsync(added);
        }
    }

    /// <summary>FR-W1: right after adding a WhatsApp profile, offer to open it for the QR sign-in.</summary>
    private async Task OfferWhatsAppSignInAsync(Profile profile)
    {
        var open = await NewDialog(
            $"Sign in to \"{profile.Name}\"",
            "WhatsApp opens in its own window. Scan the QR code there with WhatsApp on your phone (Linked devices > Link a device).",
            primary: "Open WhatsApp",
            close: "Later").ShowAsync();
        if (open == ContentDialogResult.Primary && StartWhatsApp(profile))
        {
            await RefreshSoonAsync();
        }
    }

    // ---------- Rename and remove (FR-5) ----------

    private async void RenameMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileRow { Profile: { } profile })
        {
            return;
        }

        if (await AskForName($"Rename \"{profile.Name}\"", "Rename", profile.Name, profile.Slug) is not var (name, _)
            || name == profile.Name)
        {
            return;
        }

        Profile renamed;
        try
        {
            renamed = _profiles.Rename(profile.Slug, name);
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            ShowErrors($"Couldn't rename the profile: {ex.Message}");
            return;
        }

        await RefreshAsync();

        // Keep the desktop shortcut's name in step with the profile.
        var shortcutError = TryShortcut(() => _shortcuts.Rename(renamed.Slug, renamed.Name, IconFor(renamed.App), renamed.App));
        if (shortcutError is not null)
        {
            ShowErrors($"Renamed the profile, but couldn't update its desktop shortcut: {shortcutError}");
        }
    }

    private async void RemoveMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileRow { Profile: { } profile } row || row.IsRunning)
        {
            return;
        }

        var hasShortcut = _shortcuts.Find(profile.Slug) is not null;
        var confirm = await NewDialog(
            $"Remove \"{profile.Name}\"?",
            hasShortcut
                ? "It will no longer appear in the launcher, and its desktop shortcut will be deleted."
                : "It will no longer appear in the launcher.",
            primary: "Remove",
            close: "Cancel").ShowAsync();
        if (confirm != ContentDialogResult.Primary)
        {
            return;
        }

        // Second confirmation; keeping the folders is the default (FR-5).
        var deleteFolders = false;
        var folders = _profiles.ExistingFolders(profile.Slug);
        if (folders.Count > 0)
        {
            var folderDialog = NewDialog(
                "Also delete its folders?",
                MonoList(
                    profile.App == ProfileApp.WhatsApp
                        ? "This deletes the WhatsApp sign-in and everything WhatsApp Web stored on this PC:"
                        : "This deletes the account's sign-in, chats, settings and Claude Code config on this PC:",
                    folders),
                primary: "Keep folders",
                close: null,
                secondary: "Delete folders");
            deleteFolders = await folderDialog.ShowAsync() == ContentDialogResult.Secondary;
        }

        RemoveResult result;
        try
        {
            result = _profiles.Remove(profile.Slug, deleteFolders);
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            ShowErrors($"Couldn't remove the profile: {ex.Message}");
            return;
        }

        await RefreshAsync();

        var problems = new List<string>();
        if (result.FailedFolders.Count > 0)
        {
            problems.Add("Removed the profile, but couldn't delete these folders (maybe in use). Delete them by hand:"
                + Environment.NewLine + string.Join(Environment.NewLine, result.FailedFolders));
        }

        // A shortcut to a removed profile would only show an error, so it goes too.
        if (TryShortcut(() => _shortcuts.Delete(profile.Slug)) is { } shortcutError)
        {
            problems.Add($"Couldn't delete the profile's desktop shortcut: {shortcutError}");
        }

        if (problems.Count > 0)
        {
            ShowErrors(string.Join(Environment.NewLine + Environment.NewLine, problems));
        }
    }

    // ---------- Uninstall and remove everything (FR-9) ----------

    private async void UninstallMenu_Click(object sender, RoutedEventArgs e)
    {
        // Deleting a profile's folders while its Claude is open would fail or corrupt it.
        // The user closes Claude profiles; WhatsApp profiles the launcher quits itself (WhatsApp spec, decision 3.12).
        await RefreshAsync();
        var runningClaude = _claudeRows.Where(r => r.IsCustom && r.IsRunning).Select(r => r.Name).ToList();
        if (runningClaude.Count > 0)
        {
            await NewDialog(
                "Close these Claude windows first",
                MonoList("These profiles are still running. Close their Claude windows (or quit Claude from the tray), then try again:", runningClaude),
                primary: "OK",
                close: null).ShowAsync();
            return;
        }

        var runningWhatsApp = _whatsAppRows
            .Where(r => r is { IsWhatsAppProfile: true, IsRunning: true } && _running.WhatsAppProfiles.ContainsKey(r.Key))
            .Select(r => (r.Name, ProcessId: _running.WhatsAppProfiles[r.Key]))
            .ToList();

        var uninstaller = new Uninstaller(_paths, _shortcuts, new MsiInstalledProduct());
        var plan = await Task.Run(() => uninstaller.Plan(_profiles.Profiles, Environment.ProcessPath, AppContext.BaseDirectory));

        var oldFolders = new CheckBox { Content = "Also delete these folders that look like old profiles:" };
        var dialog = NewDialog(
            "Uninstall and remove everything?",
            new ScrollViewer { MaxHeight = 420, Content = UninstallSummary(plan, oldFolders, runningWhatsApp.Select(r => r.Name)) },
            primary: "Remove everything",
            close: "Cancel");
        dialog.DefaultButton = ContentDialogButton.Close; // Enter must not delete accounts
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _poll.Stop();
        var notQuit = await Task.Run(() => QuitAndWait(runningWhatsApp.Select(r => r.ProcessId)));
        var failed = uninstaller.DeleteData(plan, oldFolders.IsChecked == true).ToList();
        if (notQuit)
        {
            failed.Add("(A WhatsApp profile didn't close in time, so its folder may be left behind.)");
        }

        if (failed.Count > 0)
        {
            await NewDialog(
                "Some things couldn't be deleted",
                MonoList("They may be in use. Delete them by hand:", failed),
                primary: "OK",
                close: null).ShowAsync();
        }

        if (plan.PortableExe is not null)
        {
            await NewDialog(
                "Delete the launcher file",
                MonoList("Everything else is removed. The launcher can't delete the file it runs from; delete it yourself once this window closes:", new[] { plan.PortableExe }),
                primary: "OK",
                close: null).ShowAsync();
        }

        // After we exit: uninstall the MSI (if installed) and remove the unpacked runtime.
        if (uninstaller.FinishCommand(plan) is { } finish)
        {
            try
            {
                using var _ = System.Diagnostics.Process.Start(finish);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                // Data is already gone; the app itself can still be removed from Settings > Apps.
            }
        }

        Application.Current.Exit();
    }

    /// <summary>
    /// Closes WhatsApp profile windows the way their X button does, waits for them to exit, and
    /// ends any that don't within 10 seconds. True if one was still running afterwards.
    /// </summary>
    private static bool QuitAndWait(IEnumerable<int> processIds)
    {
        var stuck = false;
        foreach (var id in processIds)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(id);
                process.CloseMainWindow();
                if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                {
                    process.Kill(entireProcessTree: true);
                    stuck |= !process.WaitForExit(TimeSpan.FromSeconds(5));
                }
            }
            catch (ArgumentException)
            {
                // Already gone
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                stuck = true;
            }
        }

        return stuck;
    }

    private StackPanel UninstallSummary(UninstallPlan plan, CheckBox oldFolders, IEnumerable<string> runningWhatsApp)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "This removes Claude Multi-Account Launcher and everything it created on this PC. "
                + "The profiles' sign-ins, chats and Claude Code settings are deleted and can't be recovered. "
                + "Your Default Claude, the official WhatsApp Desktop and their data are not touched.",
            TextWrapping = TextWrapping.Wrap,
        });

        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count > 0)
            {
                panel.Children.Add(MonoList(title, list));
            }
        }

        Section("Profiles' Claude data and Claude Code settings:", plan.Items
            .Where(i => i.Kind is UninstallItemKind.ProfileData or UninstallItemKind.ProfileConfig)
            .Select(i => $"{i.ProfileName}: {i.Path}"));
        Section("These WhatsApp profiles are open; the launcher quits them first:", runningWhatsApp);
        Section("WhatsApp profiles' data (their WhatsApp sign-ins on this PC):", plan.Items
            .Where(i => i.Kind == UninstallItemKind.WhatsAppData)
            .Select(i => $"{i.ProfileName}: {i.Path}"));
        Section("Desktop shortcuts:", plan.Items.Where(i => i.Kind == UninstallItemKind.DesktopShortcut).Select(i => i.Path));
        Section("Launcher settings:", plan.Items
            .Where(i => i.Kind is UninstallItemKind.LauncherData or UninstallItemKind.LocalLauncherData)
            .Select(i => i.Path));

        var afterExit = new List<string>();
        if (plan.ProductCode is not null)
        {
            afterExit.Add("The installed app (Settings > Apps entry, Start menu shortcut)");
        }

        if (plan.ExtractionRoot is not null)
        {
            afterExit.Add(plan.ExtractionRoot);
        }

        Section("After the launcher closes:", afterExit);

        if (plan.PortableExe is not null)
        {
            Section("You delete this file yourself afterwards:", new[] { plan.PortableExe });
        }

        if (plan.OldProfileFolders.Count > 0)
        {
            panel.Children.Add(oldFolders);
            panel.Children.Add(MonoList("Not in the profile list, so not deleted unless ticked:", plan.OldProfileFolders.Select(i => i.Path)));
        }

        return panel;
    }

    // ---------- Desktop shortcut (FR-8) ----------

    private void ShortcutMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProfileRow row)
        {
            return;
        }

        var slug = row.Key;
        var icon = IconFor(row.App);
        string? path = null;
        var error = TryShortcut(() => path = _shortcuts.Create(row.Name, slug, icon, row.App));

        if (error is not null)
        {
            ShowErrors($"Couldn't create the desktop shortcut: {error}");
            return;
        }

        var appName = row.IsWhatsApp ? "WhatsApp's" : "Claude's";
        ShowNotice(icon is null
            ? $"Created \"{Path.GetFileName(path)}\" on your desktop. {appName} icon couldn't be copied, so it uses the launcher's icon."
            : $"Created \"{Path.GetFileName(path)}\" on your desktop.");
    }

    /// <summary>The app's stable icon for shortcuts, or null for the launcher's own (FR-8, WhatsApp spec section 4).</summary>
    private string? IconFor(ProfileApp app) => app == ProfileApp.WhatsApp
        ? WhatsAppIcon.Ensure(_paths, _running.WhatsApp)
        : ClaudeIcon.Ensure(_paths, _install);

    /// <summary>Runs a shortcut operation; returns an error message or null.</summary>
    private static string? TryShortcut(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ShortcutConflictException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return ex.Message;
        }
    }

    private void ShowNotice(string message)
    {
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }

    // ---------- Dialog helpers ----------

    /// <summary>
    /// Name prompt with live validation and a preview of the folders it will use. Adding also picks
    /// the app (WhatsApp spec section 3); renaming keeps the profile's own.
    /// </summary>
    private async Task<(string Name, ProfileApp App)?> AskForName(string title, string primary, string initial, string? renamingSlug)
    {
        var renamingApp = renamingSlug is null ? (ProfileApp?)null : _profiles.Find(renamingSlug)?.App ?? ProfileApp.Claude;
        var appPicker = new RadioButtons
        {
            Header = "App",
            MaxColumns = 2,
            Items = { "Claude", "WhatsApp" },
            SelectedIndex = 0,
            Visibility = renamingApp is null ? Visibility.Visible : Visibility.Collapsed,
        };
        ProfileApp App() => renamingApp ?? (appPicker.SelectedIndex == 1 ? ProfileApp.WhatsApp : ProfileApp.Claude);

        var box = new TextBox { Header = "Display name", PlaceholderText = "e.g. Personal", Text = initial };
        var error = new TextBlock
        {
            // Theme brush lives in the app's merged theme dictionaries, not Root.Resources
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var preview = new TextBlock
        {
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Root.Resources["MonoFont"],
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        var dialog = NewDialog(title, new StackPanel { Spacing = 8, Children = { appPicker, box, error, preview } }, primary, "Cancel");

        void Validate()
        {
            var app = App();
            var message = _profiles.ValidateName(box.Text, renamingSlug, app);
            error.Text = box.Text.Length == 0 ? string.Empty : message ?? string.Empty;
            error.Visibility = string.IsNullOrEmpty(error.Text) ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = message is null;

            var slug = renamingSlug ?? _profiles.PreviewSlug(box.Text);
            var folders = app == ProfileApp.WhatsApp
                ? [_paths.WhatsAppDataFolder(slug)]
                : renamingSlug is not null
                    ? [_paths.DataFolder(slug)]
                    : new[] { _paths.DataFolder(slug), _paths.ConfigFolder(slug) };
            preview.Text = renamingSlug is not null
                ? "Folders stay the same:" + Environment.NewLine + string.Join(Environment.NewLine, folders)
                : message is null
                    ? "Creates:" + Environment.NewLine + string.Join(Environment.NewLine, folders)
                    : string.Empty;
        }

        box.TextChanged += (_, _) => Validate();
        appPicker.SelectionChanged += (_, _) => Validate();
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        Validate();

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? (box.Text.Trim(), App()) : null;
    }

    private ContentDialog NewDialog(string title, object content, string primary, string? close, string? secondary = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = content is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } : content,
            PrimaryButtonText = primary,
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = Root.ActualTheme,
        };

        if (close is not null)
        {
            dialog.CloseButtonText = close;
        }

        if (secondary is not null)
        {
            dialog.SecondaryButtonText = secondary;
        }

        return dialog;
    }

    private StackPanel MonoList(string intro, IEnumerable<string> lines)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join(Environment.NewLine, lines),
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Root.Resources["MonoFont"],
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        return panel;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
