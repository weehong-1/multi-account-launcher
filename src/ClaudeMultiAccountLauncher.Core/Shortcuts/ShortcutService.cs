using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Startup;

namespace ClaudeMultiAccountLauncher.Core.Shortcuts;

public sealed class ShortcutConflictException(string path)
    : Exception($"\"{System.IO.Path.GetFileName(path)}\" already exists on the desktop and wasn't made by this launcher. Rename or delete it first.")
{
    public string ShortcutPath { get; } = path;
}

/// <summary>
/// Desktop shortcuts that run <c>launcher --launch &lt;slug&gt;</c>: "Claude - &lt;name&gt;" (FR-8) and
/// "WhatsApp - &lt;name&gt;" (WhatsApp spec section 4). A shortcut is ours when it targets an exe with
/// the launcher's file name; it belongs to a profile when its arguments are that profile's
/// <c>--launch</c> line. Slugs are shared across apps, so the arguments alone identify it.
/// </summary>
public sealed class ShortcutService(string desktopFolder, string launcherExePath)
{
    public const string NamePrefix = "Claude - ";

    public const string WhatsAppNamePrefix = "WhatsApp - ";

    private static readonly string[] Prefixes = [NamePrefix, WhatsAppNamePrefix];

    public string DesktopFolder { get; } = desktopFolder;

    public static string PrefixFor(ProfileApp app) => app == ProfileApp.WhatsApp ? WhatsAppNamePrefix : NamePrefix;

    public string PathFor(string displayName, ProfileApp app = ProfileApp.Claude) =>
        Path.Combine(DesktopFolder, PrefixFor(app) + displayName + ".lnk");

    public static string ArgumentsFor(string slug) => $"{StartupCommand.LaunchSwitch} {slug}";

    /// <summary>Our shortcut for <paramref name="slug"/>, whatever it is called now, or null.</summary>
    public string? Find(string slug)
    {
        var wanted = ArgumentsFor(slug);
        return Ours().FirstOrDefault(s => string.Equals(s.Info.Arguments.Trim(), wanted, StringComparison.OrdinalIgnoreCase)).Path;
    }

    /// <summary>
    /// Every "Claude - *" and "WhatsApp - *" shortcut this launcher made, including ones for
    /// profiles removed since. Used by the full uninstall.
    /// </summary>
    public IReadOnlyList<string> FindAll() => Ours().Select(s => s.Path).ToList();

    private IEnumerable<(string Path, ShellLinkInfo Info)> Ours()
    {
        if (!Directory.Exists(DesktopFolder))
        {
            yield break;
        }

        foreach (var lnk in Prefixes.SelectMany(prefix => Directory.EnumerateFiles(DesktopFolder, prefix + "*.lnk")))
        {
            var info = TryRead(lnk);
            if (info is not null && IsOurs(info) && info.Arguments.TrimStart().StartsWith(StartupCommand.LaunchSwitch, StringComparison.OrdinalIgnoreCase))
            {
                yield return (lnk, info);
            }
        }
    }

    /// <summary>
    /// Creates (or refreshes) the shortcut. An older shortcut for the same profile under a
    /// different name is removed, so renaming a profile and recreating doesn't leave two.
    /// </summary>
    /// <param name="iconPath">Stable .ico path, or null to use the launcher's own icon.</param>
    public string Create(string displayName, string slug, string? iconPath, ProfileApp app = ProfileApp.Claude)
    {
        var path = PathFor(displayName, app);
        if (File.Exists(path) && TryRead(path) is var existing && (existing is null || !IsOurs(existing)))
        {
            throw new ShortcutConflictException(path);
        }

        var previous = Find(slug);

        ShellLink.Write(path, new ShellLinkInfo(
            launcherExePath,
            ArgumentsFor(slug),
            Path.GetDirectoryName(launcherExePath) ?? string.Empty,
            $"Open {(app == ProfileApp.WhatsApp ? "WhatsApp" : "Claude")} as \"{displayName}\"",
            iconPath ?? string.Empty));

        if (previous is not null && !string.Equals(previous, path, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(previous);
        }

        return path;
    }

    /// <summary>After a rename: if the profile has a shortcut, recreate it under the new name.</summary>
    public string? Rename(string slug, string newDisplayName, string? iconPath, ProfileApp app = ProfileApp.Claude) =>
        Find(slug) is null ? null : Create(newDisplayName, slug, iconPath, app);

    /// <summary>Deletes the profile's shortcut if there is one. Returns its path.</summary>
    public string? Delete(string slug)
    {
        var path = Find(slug);
        if (path is not null)
        {
            File.Delete(path);
        }

        return path;
    }

    private bool IsOurs(ShellLinkInfo info) =>
        string.Equals(Path.GetFileName(info.TargetPath), Path.GetFileName(launcherExePath), StringComparison.OrdinalIgnoreCase);

    private static ShellLinkInfo? TryRead(string lnk)
    {
        try
        {
            return ShellLink.Read(lnk);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
