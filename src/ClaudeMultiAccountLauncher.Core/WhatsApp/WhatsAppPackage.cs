namespace ClaudeMultiAccountLauncher.Core.WhatsApp;

/// <summary>
/// The official WhatsApp Desktop (Microsoft Store), the WhatsApp Default (WhatsApp spec section 2).
/// Values from the 2026-11-22 spike: package 5319275A.WhatsAppDesktop 2.2645.101.0.
/// </summary>
public static class WhatsAppPackage
{
    public const string Name = "5319275A.WhatsAppDesktop";

    public const string PublisherId = "cv1g1gvanyjgm";

    public const string FamilyName = Name + "_" + PublisherId;

    /// <summary>Started through the shell: the package has no execution alias.</summary>
    public const string ShellTarget = @"shell:AppsFolder\" + FamilyName + "!App";

    /// <summary>
    /// The package's only executable. Its background push process also hosts the window when
    /// WhatsApp is opened, so running is told by the window (ProcessScanner).
    /// </summary>
    public const string ExeName = "WhatsApp.Root.exe";

    /// <summary>Matches <c>5319275A.WhatsAppDesktop_&lt;version&gt;_&lt;arch&gt;__cv1g1gvanyjgm</c>.</summary>
    public static bool IsPackageFolderName(string folderName) =>
        folderName.StartsWith(Name + "_", StringComparison.OrdinalIgnoreCase)
        && folderName.EndsWith("__" + PublisherId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="executablePath"/> is <c>WhatsApp.Root.exe</c> inside any version
    /// of the package, so a process still running from the previous version after an update counts.
    /// </summary>
    public static bool OwnsExecutable(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)
            || !string.Equals(Path.GetFileName(executablePath), ExeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var dir = Path.GetDirectoryName(Path.GetFullPath(executablePath)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (IsPackageFolderName(Path.GetFileName(dir)))
            {
                return true;
            }
        }

        return false;
    }
}
