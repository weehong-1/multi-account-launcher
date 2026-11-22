using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cmal-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LauncherPaths _paths;
    private readonly ProfileStore _store;

    public ProfileServiceTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        _store = new ProfileStore(_paths.ProfilesFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ProfileService NewService()
    {
        var service = new ProfileService(_store, _paths);
        service.Load();
        return service;
    }

    [Fact]
    public void Add_creates_both_folders_and_saves_the_profile()
    {
        var profile = NewService().Add("  Personal ");

        Assert.Equal("Personal", profile.Name);
        Assert.Equal("personal", profile.Slug);
        Assert.True(Directory.Exists(_paths.DataFolder("personal")));
        Assert.True(Directory.Exists(_paths.ConfigFolder("personal")));
        Assert.Equal([profile], _store.Load());
    }

    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData("   ", "Enter a name.")]
    [InlineData("default", "\"Default\" is the built-in profile.")]
    [InlineData("a/b", "A name can't contain any of these: \\ / : * ? \" < > |")]
    [InlineData("what?", "A name can't contain any of these: \\ / : * ? \" < > |")]
    public void ValidateName_rejects_bad_names(string name, string expected) =>
        Assert.Equal(expected, NewService().ValidateName(name));

    [Fact]
    public void ValidateName_rejects_too_long() =>
        Assert.NotNull(NewService().ValidateName(new string('a', ProfileService.MaxNameLength + 1)));

    [Fact]
    public void Duplicate_names_are_rejected_ignoring_case()
    {
        var service = NewService();
        service.Add("Personal");

        Assert.Equal("A profile with this name already exists.", service.ValidateName("personal"));
        Assert.Throws<ProfileValidationException>(() => service.Add("PERSONAL"));
    }

    [Fact]
    public void Names_that_slug_the_same_get_distinct_slugs()
    {
        var service = NewService();
        var a = service.Add("Work");
        var b = service.Add("Work!");

        Assert.Equal("work", a.Slug);
        Assert.Equal("work-2", b.Slug);
    }

    [Fact]
    public void Rename_keeps_slug_and_allows_same_name_in_new_case()
    {
        var service = NewService();
        service.Add("Personal");

        var renamed = service.Rename("personal", "PERSONAL");
        Assert.Equal("PERSONAL", renamed.Name);
        Assert.Equal("personal", renamed.Slug);

        var again = service.Rename("personal", "Home");
        Assert.Equal("Home", again.Name);
        Assert.Equal("personal", again.Slug);
        Assert.Equal("Home", Assert.Single(_store.Load()).Name);
    }

    [Fact]
    public void Rename_to_another_profiles_name_is_rejected()
    {
        var service = NewService();
        service.Add("Personal");
        service.Add("Work");

        Assert.Throws<ProfileValidationException>(() => service.Rename("work", "personal"));
    }

    [Fact]
    public void Remove_keeps_folders_by_default()
    {
        var service = NewService();
        service.Add("Personal");

        var result = service.Remove("personal", deleteFolders: false);

        Assert.Empty(result.FailedFolders);
        Assert.Empty(_store.Load());
        Assert.True(Directory.Exists(_paths.DataFolder("personal")));
        Assert.True(Directory.Exists(_paths.ConfigFolder("personal")));
    }

    [Fact]
    public void Remove_with_delete_removes_all_folders_including_the_msix_redirected_one()
    {
        var service = NewService();
        service.Add("Personal");
        Directory.CreateDirectory(_paths.RedirectedDataFolder("personal"));
        File.WriteAllText(Path.Combine(_paths.DataFolder("personal"), "Preferences"), "{}");

        Assert.Equal(3, service.ExistingFolders("personal").Count);
        var result = service.Remove("personal", deleteFolders: true);

        Assert.Empty(result.FailedFolders);
        Assert.False(Directory.Exists(_paths.DataFolder("personal")));
        Assert.False(Directory.Exists(_paths.ConfigFolder("personal")));
        Assert.False(Directory.Exists(_paths.RedirectedDataFolder("personal")));
    }

    [Fact]
    public void Remove_reports_folders_it_could_not_delete()
    {
        var service = NewService();
        service.Add("Personal");
        var locked = Path.Combine(_paths.DataFolder("personal"), "LOCK");

        using (File.Open(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var result = service.Remove("personal", deleteFolders: true);
            Assert.Equal([_paths.DataFolder("personal")], result.FailedFolders);
        }

        Assert.Empty(_store.Load());
    }

    [Fact]
    public void Running_maps_instances_to_default_and_profiles()
    {
        var personal = new Profile("Personal", "personal", DateTimeOffset.UnixEpoch);
        var work = new Profile("Work", "work", DateTimeOffset.UnixEpoch);
        RunningInstance[] running = [new(1, null), new(2, _paths.DataFolder("personal").ToUpperInvariant() + "\\")];

        Assert.True(RunningProfiles.IsRunning(null, running, _paths));
        Assert.True(RunningProfiles.IsRunning(personal, running, _paths));
        Assert.False(RunningProfiles.IsRunning(work, running, _paths));
        Assert.False(RunningProfiles.IsRunning(null, [new RunningInstance(2, "C:\\x")], _paths));
    }

    [Fact]
    public void Add_whatsapp_creates_only_its_webview2_folder()
    {
        var profile = NewService().Add("Resetrix", ProfileApp.WhatsApp);

        Assert.Equal(ProfileApp.WhatsApp, profile.App);
        Assert.Equal(
            Path.Combine(_root, "Local", "claude-multi-account-launcher", "whatsapp", "resetrix"),
            _paths.WhatsAppDataFolder("resetrix"));
        Assert.True(Directory.Exists(_paths.WhatsAppDataFolder("resetrix")));
        Assert.False(Directory.Exists(_paths.DataFolder("resetrix")));
        Assert.False(Directory.Exists(_paths.ConfigFolder("resetrix")));
        Assert.Equal(profile, Assert.Single(_store.Load()));
    }

    [Fact]
    public void Slugs_are_shared_across_apps()
    {
        var service = NewService();
        var claude = service.Add("Personal");
        var whatsApp = service.Add("Personal", ProfileApp.WhatsApp);

        Assert.Equal("personal", claude.Slug);
        Assert.Equal("personal-2", whatsApp.Slug);
    }

    [Fact]
    public void Names_are_unique_per_app()
    {
        var service = NewService();
        service.Add("Personal");
        var whatsApp = service.Add("Personal", ProfileApp.WhatsApp);

        Assert.NotNull(service.ValidateName("personal"));
        Assert.NotNull(service.ValidateName("PERSONAL", app: ProfileApp.WhatsApp));
        Assert.NotNull(service.ValidateName("Default", app: ProfileApp.WhatsApp));
        Assert.Null(service.ValidateName("Work", app: ProfileApp.WhatsApp));

        // Renaming checks the profile's own app: the Claude "Personal" doesn't block it
        Assert.Null(service.ValidateName("Personal", whatsApp.Slug));
    }

    [Fact]
    public void Remove_whatsapp_with_delete_removes_its_folder()
    {
        var service = NewService();
        var profile = service.Add("Resetrix", ProfileApp.WhatsApp);
        Assert.Equal([_paths.WhatsAppDataFolder(profile.Slug)], service.ExistingFolders(profile.Slug));

        var result = service.Remove(profile.Slug, deleteFolders: true);

        Assert.Empty(result.FailedFolders);
        Assert.False(Directory.Exists(_paths.WhatsAppDataFolder(profile.Slug)));
        Assert.Empty(service.Profiles);
    }
}
