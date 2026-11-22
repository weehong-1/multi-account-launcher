using Windows.Management.Deployment;

namespace ClaudeMultiAccountLauncher.Core.WhatsApp;

/// <summary>An installed official WhatsApp Desktop.</summary>
/// <param name="InstallFolder">The package's install location (versioned; changes on update).</param>
public sealed record WhatsAppDesktop(string InstallFolder);

/// <summary>Seam so the lookup can be tested without the package installed.</summary>
public interface IWhatsAppProbe
{
    /// <summary>Install location of the WhatsApp package for the current user, or null.</summary>
    string? FindInstallLocation();
}

/// <summary>
/// Finds the official WhatsApp Desktop. Not installed is normal: the WhatsApp Default is then not
/// listed, and WhatsApp profiles still work (WhatsApp spec section 2).
/// </summary>
public sealed class WhatsAppLocator(IWhatsAppProbe probe)
{
    public WhatsAppDesktop? Locate() => probe.FindInstallLocation() is { } folder ? new WhatsAppDesktop(folder) : null;
}

/// <summary>Package manager lookup, current user, no admin rights.</summary>
public sealed class SystemWhatsAppProbe : IWhatsAppProbe
{
    public string? FindInstallLocation()
    {
        try
        {
            var package = new PackageManager()
                .FindPackagesForUser(string.Empty, WhatsAppPackage.FamilyName)
                .OrderByDescending(p => p.Id.Version.Major)
                .ThenByDescending(p => p.Id.Version.Minor)
                .ThenByDescending(p => p.Id.Version.Build)
                .ThenByDescending(p => p.Id.Version.Revision)
                .FirstOrDefault();

            return package?.InstalledLocation.Path;
        }
        catch (Exception)
        {
            // Package APIs can fail on stripped-down Windows; treat as not installed.
            return null;
        }
    }
}
