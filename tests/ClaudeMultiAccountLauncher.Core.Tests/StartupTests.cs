using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Startup;
using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public sealed class StartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cmal-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LauncherPaths _paths;
    private readonly ProfileStore _store;
    private readonly List<(ClaudeInstall Install, Profile? Profile)> _started = [];
    private readonly Starter _starter;

    public StartupTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        _store = new ProfileStore(_paths.ProfilesFile);
        _starter = new Starter(_started);
    }

    private sealed class Starter(List<(ClaudeInstall Install, Profile? Profile)> claude) : IAppStarter
    {
        public List<WhatsAppDesktop> WhatsAppDefault { get; } = [];

        public List<Profile> WhatsAppProfiles { get; } = [];

        public void StartClaude(ClaudeInstall install, Profile? profile) => claude.Add((install, profile));

        public void StartWhatsAppDefault(WhatsAppDesktop desktop) => WhatsAppDefault.Add(desktop);

        public void StartWhatsAppProfile(Profile profile) => WhatsAppProfiles.Add(profile);
    }

    private sealed class WhatsAppProbe(bool found) : IWhatsAppProbe
    {
        public string? FindInstallLocation() =>
            found ? @"C:\P\5319275A.WhatsAppDesktop_2.2645.101.0_x64__cv1g1gvanyjgm" : null;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Probe(bool found) : IInstallProbe
    {
        public bool FileExists(string path) => found;

        public string? FindMsixInstallLocation() => found ? @"C:\P\Claude_1_x64__pzs8sxrjxfjjc" : null;
    }

    private QuickLauncher NewQuickLauncher(bool claudeFound = true, bool whatsAppFound = true) =>
        new(_store, new ClaudeLocator(_paths, new Probe(claudeFound)), new WhatsAppLocator(new WhatsAppProbe(whatsAppFound)), _starter);

    [Theory]
    [InlineData(new[] { "--launch", "personal" }, "personal")]
    [InlineData(new[] { "--launch=Personal" }, "personal")]
    [InlineData(new[] { "--LAUNCH", "default" }, "default")]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--something" }, null)]
    public void Parses_launch_switch(string[] args, string? expected)
    {
        var cmd = StartupCommand.Parse(args);
        Assert.Equal(expected, cmd.LaunchSlug);
        Assert.Null(cmd.Error);
    }

    [Theory]
    [InlineData("--launch")]
    [InlineData("--launch=")]
    [InlineData("--launch| ")]
    public void Launch_without_slug_is_an_error(string argsJoinedByBar)
    {
        var cmd = StartupCommand.Parse(argsJoinedByBar.Split('|'));
        Assert.Null(cmd.LaunchSlug);
        Assert.NotNull(cmd.Error);
    }

    [Fact]
    public void Default_launches_with_no_profile()
    {
        var outcome = NewQuickLauncher().Run(StartupCommand.DefaultSlug);

        Assert.IsType<QuickLaunchOutcome.Launched>(outcome);
        Assert.Null(Assert.Single(_started).Profile);
    }

    [Fact]
    public void Profile_launches_directly()
    {
        var personal = new Profile("Personal", "personal", DateTimeOffset.UnixEpoch);
        _store.Save([personal]);

        var outcome = NewQuickLauncher().Run("personal");

        Assert.IsType<QuickLaunchOutcome.Launched>(outcome);
        Assert.Equal(personal, Assert.Single(_started).Profile);
    }

    [Fact]
    public void Unknown_profile_fails_with_a_message()
    {
        var outcome = NewQuickLauncher().Run("gone");

        Assert.Contains("no longer exists", Assert.IsType<QuickLaunchOutcome.Failed>(outcome).Message);
        Assert.Empty(_started);
    }

    [Fact]
    public void Claude_not_found_fails_with_the_paths_tried()
    {
        var outcome = NewQuickLauncher(claudeFound: false).Run(StartupCommand.DefaultSlug);

        Assert.Contains("Tried:", Assert.IsType<QuickLaunchOutcome.Failed>(outcome).Message);
    }

    [Theory]
    [InlineData(new[] { "--whatsapp", "resetrix" }, "resetrix")]
    [InlineData(new[] { "--WhatsApp=Personal-2" }, "personal-2")]
    public void Parses_whatsapp_switch(string[] args, string expected)
    {
        var cmd = StartupCommand.Parse(args);
        Assert.Equal(expected, cmd.WhatsAppSlug);
        Assert.Null(cmd.LaunchSlug);
        Assert.Null(cmd.Error);
    }

    [Fact]
    public void Whatsapp_without_slug_is_an_error() =>
        Assert.Contains("--whatsapp needs a profile", StartupCommand.Parse(["--whatsapp"]).Error);

    [Fact]
    public void Whatsapp_profile_shortcut_starts_its_window_not_claude()
    {
        var resetrix = new Profile("Resetrix", "resetrix", DateTimeOffset.UnixEpoch, ProfileApp.WhatsApp);
        _store.Save([resetrix]);

        var outcome = NewQuickLauncher(claudeFound: false).Run("resetrix");

        Assert.IsType<QuickLaunchOutcome.Launched>(outcome);
        Assert.Equal(resetrix, Assert.Single(_starter.WhatsAppProfiles));
        Assert.Empty(_started);
    }

    [Fact]
    public void Whatsapp_default_starts_the_official_app()
    {
        var outcome = NewQuickLauncher().Run(StartupCommand.WhatsAppDefaultSlug);

        Assert.IsType<QuickLaunchOutcome.Launched>(outcome);
        Assert.Single(_starter.WhatsAppDefault);
        Assert.Empty(_started);
    }

    [Fact]
    public void Whatsapp_default_not_installed_fails_with_a_message()
    {
        var outcome = NewQuickLauncher(whatsAppFound: false).Run(StartupCommand.WhatsAppDefaultSlug);

        Assert.Contains("isn't installed", Assert.IsType<QuickLaunchOutcome.Failed>(outcome).Message);
        Assert.Empty(_starter.WhatsAppDefault);
    }

    [Fact]
    public void Whatsapp_profile_process_runs_the_launcher_with_its_slug()
    {
        var launcher = new WhatsAppLauncher(_paths, @"C:\L\claude-multi-account-launcher.exe");
        var psi = launcher.ProfileStartInfo(new Profile("Resetrix", "resetrix", DateTimeOffset.UnixEpoch, ProfileApp.WhatsApp));

        Assert.Equal(@"C:\L\claude-multi-account-launcher.exe", psi.FileName);
        Assert.Equal(["--whatsapp", "resetrix"], psi.ArgumentList);
        Assert.Equal(@"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", WhatsAppLauncher.DefaultStartInfo().FileName);
        Assert.Throws<ArgumentException>(() => launcher.ProfileStartInfo(new Profile("Work", "work", DateTimeOffset.UnixEpoch)));
    }
}
