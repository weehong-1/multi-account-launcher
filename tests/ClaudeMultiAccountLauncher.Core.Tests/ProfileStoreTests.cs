using ClaudeMultiAccountLauncher.Core.Profiles;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cmal-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "sub", "profiles.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_means_no_profiles() =>
        Assert.Empty(new ProfileStore(FilePath).Load());

    [Fact]
    public void Round_trips_profiles_and_creates_the_folder()
    {
        var store = new ProfileStore(FilePath);
        var created = new DateTimeOffset(2026, 11, 19, 9, 30, 0, TimeSpan.FromHours(8));
        Profile[] profiles =
        [
            new("Personal", "personal", created),
            new("Resetrix", "resetrix", created),
        ];

        store.Save(profiles);

        Assert.Equal(profiles, store.Load());
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Writes_the_spec_field_names()
    {
        var store = new ProfileStore(FilePath);
        store.Save([new("Personal", "personal", DateTimeOffset.UnixEpoch)]);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"name\": \"Personal\"", json);
        Assert.Contains("\"slug\": \"personal\"", json);
        Assert.Contains("\"createdAt\":", json);
        Assert.DoesNotContain("status", json);
    }

    [Fact]
    public void Older_files_with_a_status_field_still_load()
    {
        // Written by the first 0.1.0 build, which tracked sign-in status
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            { "version": 1, "profiles": [
              { "name": "Claude Code", "slug": "claude-code", "status": "notSignedIn", "createdAt": "2026-11-19T14:45:11+08:00" } ] }
            """);

        var profile = Assert.Single(new ProfileStore(FilePath).Load());

        Assert.Equal(new Profile("Claude Code", "claude-code", new DateTimeOffset(2026, 11, 19, 14, 45, 11, TimeSpan.FromHours(8))), profile);
    }

    [Fact]
    public void Entries_without_app_are_read_as_claude()
    {
        // Written before WhatsApp support
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            { "version": 1, "profiles": [
              { "name": "Personal", "slug": "personal", "createdAt": "2026-11-19T14:45:11+08:00" } ] }
            """);

        Assert.Equal(ProfileApp.Claude, Assert.Single(new ProfileStore(FilePath).Load()).App);
    }

    [Fact]
    public void Round_trips_the_app_as_claude_or_whatsapp()
    {
        var store = new ProfileStore(FilePath);
        Profile[] profiles =
        [
            new("Personal", "personal", DateTimeOffset.UnixEpoch),
            new("Personal", "personal-2", DateTimeOffset.UnixEpoch, ProfileApp.WhatsApp),
        ];

        store.Save(profiles);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"app\": \"claude\"", json);
        Assert.Contains("\"app\": \"whatsapp\"", json);
        Assert.Equal(profiles, store.Load());
    }

    [Fact]
    public void Corrupt_file_throws_and_is_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Throws<ProfileStoreException>(() => new ProfileStore(FilePath).Load());
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Newer_format_version_is_refused()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """{ "version": 99, "profiles": [] }""");

        Assert.Throws<ProfileStoreException>(() => new ProfileStore(FilePath).Load());
    }
}
