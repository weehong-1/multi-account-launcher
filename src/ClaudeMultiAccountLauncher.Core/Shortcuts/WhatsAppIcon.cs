using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Shortcuts;

/// <summary>
/// Keeps one stable copy of the official WhatsApp icon for WhatsApp shortcuts and windows
/// (WhatsApp spec section 4, as Claude spec FR-8 does for Claude). The package folder is
/// versioned and replaced on update, so nothing points into it.
/// </summary>
/// <remarks>
/// WhatsApp.Root.exe has no icon resource (checked 2026-11-22); like other Store apps its icon
/// is a set of PNGs in the package (the app list logo, <c>Assets\AppList.targetsize-N*.png</c>).
/// The transparent "unplated" ones are the ones the taskbar uses.
/// </remarks>
public static class WhatsAppIcon
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 256];

    /// <summary>
    /// Returns the stable .ico, building it the first time. Null when it can't be made (e.g.
    /// WhatsApp Desktop isn't installed): shortcuts then use the launcher's own icon.
    /// </summary>
    public static string? Ensure(LauncherPaths paths, WhatsAppDesktop? desktop)
    {
        if (File.Exists(paths.WhatsAppIconFile))
        {
            return paths.WhatsAppIconFile;
        }

        if (desktop is null)
        {
            return null;
        }

        try
        {
            var pngs = new List<byte[]>();
            foreach (var size in Sizes)
            {
                var png = new[] { $"AppList.targetsize-{size}_altform-unplated.png", $"AppList.targetsize-{size}.png" }
                    .Select(name => Path.Combine(desktop.InstallFolder, "Assets", name))
                    .FirstOrDefault(File.Exists);
                if (png is not null)
                {
                    pngs.Add(File.ReadAllBytes(png));
                }
            }

            var ico = IcoFile.FromPngs(pngs);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.WhatsAppIconFile)!);
            File.WriteAllBytes(paths.WhatsAppIconFile, ico);
            return paths.WhatsAppIconFile;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
