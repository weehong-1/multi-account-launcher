using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Shortcuts;
using ClaudeMultiAccountLauncher.Core.Uninstall;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public sealed class UninstallerTests : IDisposable
{
    private const string ProductCode = "{11111111-2222-3333-4444-555555555555}";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cmal-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LauncherPaths _paths;
    private readonly string _desktop;
    private readonly string _installedExe;
    private readonly ShortcutService _shortcuts;

    private static readonly Profile Personal = new("Personal", "personal", DateTimeOffset.UnixEpoch);
    private static readonly Profile Work = new("Work", "work", DateTimeOffset.UnixEpoch);

    public UninstallerTests()
    {
        _paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        _desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        _installedExe = Path.Combine(_paths.LocalAppData, InstallerInfo.InstallFolderRelative, "claude-multi-account-launcher.exe");
        _shortcuts = new ShortcutService(_desktop, _installedExe);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeProduct(string? code) : IInstalledProduct
    {
        public string? FindProductCode() => code;
    }

    private Uninstaller NewUninstaller(string? productCode = ProductCode) => new(_paths, _shortcuts, new FakeProduct(productCode));

    /// <summary>A PC with two profiles, their shortcuts, the launcher's data, and Default's data.</summary>
    private void SeedMachine()
    {
        foreach (var slug in new[] { "personal", "work" })
        {
            Directory.CreateDirectory(_paths.DataFolder(slug));
            File.WriteAllText(Path.Combine(_paths.DataFolder(slug), "Cookies"), "x");
            Directory.CreateDirectory(_paths.ConfigFolder(slug));
        }

        Directory.CreateDirectory(_paths.RedirectedDataFolder("personal"));
        Directory.CreateDirectory(Path.Combine(_paths.LocalAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude")); // Default
        Directory.CreateDirectory(_paths.AppFolder);
        File.WriteAllText(_paths.ProfilesFile, "{}");
        _shortcuts.Create("Personal", "personal", null);
        _shortcuts.Create("Default", "default", null);
        _shortcuts.Create("Gone", "gone", null); // profile removed earlier
        ShellLink.Write(Path.Combine(_desktop, "Claude - Mine.lnk"), new ShellLinkInfo(@"C:\Other\tool.exe", "", @"C:\Other", "", ""));
    }

    [Fact]
    public void Plan_lists_everything_the_launcher_made_and_nothing_else()
    {
        SeedMachine();

        var plan = NewUninstaller().Plan([Personal, Work], _installedExe, appBaseDirectory: null);

        var paths = plan.Items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(_paths.DataFolder("personal"), paths);
        Assert.Contains(_paths.RedirectedDataFolder("personal"), paths);
        Assert.Contains(_paths.ConfigFolder("personal"), paths);
        Assert.Contains(_paths.DataFolder("work"), paths);
        Assert.Contains(_paths.ConfigFolder("work"), paths);
        Assert.Contains(_paths.AppFolder, paths);
        Assert.Contains(Path.Combine(_desktop, "Claude - Personal.lnk"), paths);
        Assert.Contains(Path.Combine(_desktop, "Claude - Default.lnk"), paths);
        Assert.Contains(Path.Combine(_desktop, "Claude - Gone.lnk"), paths);

        Assert.DoesNotContain(Path.Combine(_desktop, "Claude - Mine.lnk"), paths); // not ours
        Assert.DoesNotContain(paths, p => p.EndsWith(@"LocalCache\Roaming\Claude", StringComparison.OrdinalIgnoreCase)); // Default
        Assert.Equal(ProductCode, plan.ProductCode);
        Assert.Null(plan.PortableExe);
    }

    [Fact]
    public void Folders_of_profiles_no_longer_listed_are_offered_separately()
    {
        SeedMachine();
        Directory.CreateDirectory(_paths.DataFolder("old"));
        Directory.CreateDirectory(_paths.ConfigFolder("old"));

        var plan = NewUninstaller().Plan([Personal, Work], _installedExe, null);

        Assert.Equal(
            new[] { _paths.DataFolder("old"), _paths.ConfigFolder("old") }.Order(),
            plan.OldProfileFolders.Select(i => i.Path).Order());
        Assert.DoesNotContain(plan.Items, i => i.Path.Contains("-old"));
    }

    [Fact]
    public void DeleteData_removes_every_item_and_keeps_Default_and_foreign_files()
    {
        SeedMachine();
        Directory.CreateDirectory(_paths.DataFolder("old"));
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([Personal, Work], _installedExe, null);

        var failed = uninstaller.DeleteData(plan, includeOldProfileFolders: false);

        Assert.Empty(failed);
        Assert.All(plan.Items, i => Assert.False(File.Exists(i.Path) || Directory.Exists(i.Path), i.Path));
        Assert.True(Directory.Exists(_paths.DataFolder("old")), "old folder kept unless ticked");
        Assert.True(File.Exists(Path.Combine(_desktop, "Claude - Mine.lnk")));
        Assert.True(Directory.Exists(Path.Combine(_paths.LocalAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude")));
    }

    [Fact]
    public void DeleteData_removes_old_profile_folders_when_ticked()
    {
        SeedMachine();
        Directory.CreateDirectory(_paths.DataFolder("old"));
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([Personal], _installedExe, null);

        uninstaller.DeleteData(plan, includeOldProfileFolders: true);

        Assert.False(Directory.Exists(_paths.DataFolder("old")));
        Assert.False(Directory.Exists(_paths.DataFolder("work"))); // not in the list, so it was an old folder too
    }

    [Fact]
    public void DeleteData_reports_what_it_could_not_delete_and_carries_on()
    {
        SeedMachine();
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([Personal, Work], _installedExe, null);

        using (File.Open(Path.Combine(_paths.DataFolder("personal"), "LOCK"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = uninstaller.DeleteData(plan, includeOldProfileFolders: false);
            Assert.Equal([_paths.DataFolder("personal")], failed);
        }

        Assert.False(Directory.Exists(_paths.DataFolder("work")));
        Assert.False(Directory.Exists(_paths.AppFolder));
    }

    private static readonly Profile Sales = new("Sales", "sales", DateTimeOffset.UnixEpoch, ProfileApp.WhatsApp);

    /// <summary>A WhatsApp profile, a WhatsApp folder kept after Remove, and the official app's data.</summary>
    private string SeedWhatsApp()
    {
        Directory.CreateDirectory(_paths.WhatsAppDataFolder("sales"));
        File.WriteAllText(Path.Combine(_paths.WhatsAppDataFolder("sales"), "Cookies"), "x");
        Directory.CreateDirectory(_paths.WhatsAppDataFolder("kept"));
        _shortcuts.Create("Sales", "sales", null, ProfileApp.WhatsApp);
        _shortcuts.Create("Default", "whatsapp-default", null, ProfileApp.WhatsApp);
        var official = Path.Combine(_paths.LocalAppData, "Packages", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm", "LocalState");
        Directory.CreateDirectory(official);
        return official;
    }

    [Fact]
    public void Plan_lists_whatsapp_data_shortcuts_and_the_local_folder()
    {
        SeedMachine();
        SeedWhatsApp();

        var plan = NewUninstaller().Plan([Personal, Work, Sales], _installedExe, null);

        Assert.Contains(plan.Items, i => i is { Kind: UninstallItemKind.WhatsAppData, ProfileName: "Sales" } && i.Path == _paths.WhatsAppDataFolder("sales"));
        Assert.Contains(plan.Items, i => i.Kind == UninstallItemKind.DesktopShortcut && i.Path == Path.Combine(_desktop, "WhatsApp - Sales.lnk"));
        Assert.Contains(plan.Items, i => i.Kind == UninstallItemKind.DesktopShortcut && i.Path == Path.Combine(_desktop, "WhatsApp - Default.lnk"));
        Assert.Contains(plan.Items, i => i.Kind == UninstallItemKind.LocalLauncherData && i.Path == _paths.LocalAppFolder);
        Assert.DoesNotContain(plan.Items, i => i.Path == _paths.DataFolder("sales")); // not a Claude profile
        Assert.Equal([_paths.WhatsAppDataFolder("kept")], plan.OldProfileFolders.Select(i => i.Path));
    }

    [Fact]
    public void Unticked_old_whatsapp_folders_survive_removing_the_local_folder()
    {
        SeedMachine();
        var official = SeedWhatsApp();
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([Personal, Work, Sales], _installedExe, null);

        var failed = uninstaller.DeleteData(plan, includeOldProfileFolders: false);

        Assert.Empty(failed);
        Assert.False(Directory.Exists(_paths.WhatsAppDataFolder("sales")));
        Assert.True(Directory.Exists(_paths.WhatsAppDataFolder("kept")), "kept unless ticked");
        Assert.False(File.Exists(Path.Combine(_desktop, "WhatsApp - Sales.lnk")));
        Assert.True(Directory.Exists(official), "the official WhatsApp's data is never touched");
    }

    [Fact]
    public void Ticked_old_whatsapp_folders_go_with_the_whole_local_folder()
    {
        SeedMachine();
        SeedWhatsApp();
        File.WriteAllText(Path.Combine(_paths.LocalAppFolder, "stray.txt"), "x");
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([Personal, Work, Sales], _installedExe, null);

        Assert.Empty(uninstaller.DeleteData(plan, includeOldProfileFolders: true));

        Assert.False(Directory.Exists(_paths.LocalAppFolder));
    }

    [Fact]
    public void Installed_run_finishes_with_msi_uninstall_and_temp_cleanup()
    {
        var extraction = Path.Combine(Path.GetTempPath(), ".net", "claude-multi-account-launcher", "abc123");
        var uninstaller = NewUninstaller();
        var plan = uninstaller.Plan([], _installedExe, extraction);

        var psi = uninstaller.FinishCommand(plan)!;

        Assert.Equal("cmd.exe", psi.FileName);
        Assert.True(psi.CreateNoWindow);
        Assert.Contains($"msiexec.exe /x {ProductCode} /passive", psi.Arguments);
        Assert.Contains($"rmdir /s /q \"{Path.Combine(Path.GetTempPath(), ".net", "claude-multi-account-launcher")}\"", psi.Arguments);
        Assert.StartsWith("/d /c \"ping -n 3 127.0.0.1 >nul & ", psi.Arguments);
    }

    [Fact]
    public void Portable_run_is_flagged_and_does_not_call_msiexec()
    {
        var portableExe = Path.Combine(_root, "Downloads", "claude-multi-account-launcher.exe");
        var uninstaller = NewUninstaller(productCode: null);

        var plan = uninstaller.Plan([], portableExe, appBaseDirectory: null);

        Assert.Equal(portableExe, plan.PortableExe);
        Assert.Null(plan.ProductCode);
        Assert.Null(uninstaller.FinishCommand(plan)); // nothing to run afterwards
    }

    [Fact]
    public void Portable_copy_next_to_an_installed_one_uninstalls_both()
    {
        var portableExe = Path.Combine(_root, "Downloads", "claude-multi-account-launcher.exe");

        var plan = NewUninstaller().Plan([], portableExe, null);

        Assert.Equal(ProductCode, plan.ProductCode);
        Assert.Equal(portableExe, plan.PortableExe);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Extraction_root_is_only_the_temp_bundle_folder(bool underTemp)
    {
        var baseDir = underTemp
            ? Path.Combine(Path.GetTempPath(), ".net", "claude-multi-account-launcher", "xyz") + Path.DirectorySeparatorChar
            : @"C:\src\bin\Debug\";

        var root = Uninstaller.ExtractionRoot(baseDir);

        if (underTemp)
        {
            Assert.Equal(Path.Combine(Path.GetTempPath(), ".net", "claude-multi-account-launcher"), root);
        }
        else
        {
            Assert.Null(root);
        }
    }
}
