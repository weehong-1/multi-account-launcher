namespace ClaudeMultiAccountLauncher.Core.Profiles;

public sealed class ProfileValidationException(string message) : Exception(message);

/// <summary>Result of removing a profile: folders that could not be deleted, if any.</summary>
public sealed record RemoveResult(IReadOnlyList<string> FailedFolders);

/// <summary>Adds, renames and removes profiles, keeping profiles.json in step (FR-2, FR-5).</summary>
public sealed class ProfileService(ProfileStore store, LauncherPaths paths, TimeProvider? clock = null)
{
    public const int MaxNameLength = 50;
    public const string DefaultProfileName = "Default";

    /// <summary>Characters Windows does not allow in a file name; the name becomes a .lnk name (FR-8).</summary>
    private static readonly char[] InvalidNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private List<Profile> _profiles = [];

    public IReadOnlyList<Profile> Profiles => _profiles;

    /// <summary>Throws <see cref="ProfileStoreException"/> if profiles.json is unreadable.</summary>
    public void Load() => _profiles = [.. store.Load()];

    public Profile? Find(string slug) => _profiles.FirstOrDefault(p => p.Slug == slug);

    /// <summary>
    /// Returns an error message, or null if <paramref name="name"/> is fine. Names are unique per
    /// app: a Claude "Personal" and a WhatsApp "Personal" can both exist (their shortcuts differ).
    /// When renaming, the app is the profile's own; otherwise <paramref name="app"/> (default Claude).
    /// </summary>
    public string? ValidateName(string name, string? renamingSlug = null, ProfileApp? app = null)
    {
        var forApp = app ?? (renamingSlug is null ? null : Find(renamingSlug)?.App) ?? ProfileApp.Claude;
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "Enter a name.";
        }

        if (trimmed.Length > MaxNameLength)
        {
            return $"Use at most {MaxNameLength} characters.";
        }

        if (trimmed.IndexOfAny(InvalidNameChars) >= 0)
        {
            return "A name can't contain any of these: \\ / : * ? \" < > |";
        }

        if (string.Equals(trimmed, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return "\"Default\" is the built-in profile.";
        }

        if (_profiles.Any(p => p.App == forApp && p.Slug != renamingSlug && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return "A profile with this name already exists.";
        }

        return null;
    }

    /// <summary>The slug a new profile with this name would get.</summary>
    public string PreviewSlug(string name) => SlugGenerator.Create(name.Trim(), _profiles.Select(p => p.Slug));

    /// <summary>
    /// Creates the profile's folders and saves it. Claude: data and config folders (FR-2).
    /// WhatsApp: its WebView2 data folder (WhatsApp spec section 6).
    /// </summary>
    public Profile Add(string name, ProfileApp app = ProfileApp.Claude)
    {
        ThrowIfInvalid(name, app: app);

        var profile = new Profile(name.Trim(), PreviewSlug(name), _clock.GetLocalNow(), app);
        foreach (var folder in OwnFolders(profile))
        {
            Directory.CreateDirectory(folder);
        }

        Save([.. _profiles, profile]);
        return profile;
    }

    /// <summary>Changes the display name only; slug and folders stay (FR-5).</summary>
    public Profile Rename(string slug, string newName)
    {
        var existing = Require(slug);
        ThrowIfInvalid(newName, slug);

        var renamed = existing with { Name = newName.Trim() };
        Save(_profiles.Select(p => p.Slug == slug ? renamed : p));
        return renamed;
    }

    /// <summary>Folders that belong to a profile, for display and deletion. Existing ones only.</summary>
    public IReadOnlyList<string> ExistingFolders(string slug)
    {
        var profile = Require(slug);
        string[] folders = profile.App == ProfileApp.WhatsApp
            ? [paths.WhatsAppDataFolder(slug)]
            : [paths.DataFolder(slug), paths.RedirectedDataFolder(slug), paths.ConfigFolder(slug)];
        return folders.Where(Directory.Exists).ToList();
    }

    /// <summary>Folders the launcher creates for a profile (not the MSIX-redirected copy).</summary>
    private string[] OwnFolders(Profile profile) => profile.App == ProfileApp.WhatsApp
        ? [paths.WhatsAppDataFolder(profile.Slug)]
        : [paths.DataFolder(profile.Slug), paths.ConfigFolder(profile.Slug)];

    /// <summary>
    /// Takes the profile off the list. Folders are kept unless <paramref name="deleteFolders"/>.
    /// The caller must make sure the profile is not running before deleting folders.
    /// </summary>
    public RemoveResult Remove(string slug, bool deleteFolders)
    {
        Require(slug);
        var folders = deleteFolders ? ExistingFolders(slug) : [];

        Save(_profiles.Where(p => p.Slug != slug));

        var failed = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(folder);
            }
        }

        return new RemoveResult(failed);
    }

    private Profile Require(string slug) =>
        Find(slug) ?? throw new ProfileValidationException($"No profile with slug \"{slug}\".");

    private void ThrowIfInvalid(string name, string? renamingSlug = null, ProfileApp? app = null)
    {
        if (ValidateName(name, renamingSlug, app) is { } error)
        {
            throw new ProfileValidationException(error);
        }
    }

    private void Save(IEnumerable<Profile> profiles)
    {
        var list = profiles.ToList();
        store.Save(list);
        _profiles = list;
    }
}
