using System.Buffers.Binary;
using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.Profiles;
using ClaudeMultiAccountLauncher.Core.Shortcuts;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public sealed class ShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cmal-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly string _launcherExe;
    private readonly ShortcutService _service;

    public ShortcutTests()
    {
        _desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        _launcherExe = Path.Combine(_root, "app", "claude-multi-account-launcher.exe");
        _service = new ShortcutService(_desktop, _launcherExe);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---------- Shortcuts (real .lnk files through the Windows shell) ----------

    [Fact]
    public void Create_writes_a_shortcut_that_runs_launch_slug()
    {
        var icon = Path.Combine(_root, "claude.ico");

        var path = _service.Create("Personal", "personal", icon);

        Assert.Equal(Path.Combine(_desktop, "Claude - Personal.lnk"), path);
        var info = ShellLink.Read(path);
        Assert.Equal(_launcherExe, info.TargetPath);
        Assert.Equal("--launch personal", info.Arguments);
        Assert.Equal(Path.GetDirectoryName(_launcherExe), info.WorkingDirectory);
        Assert.Equal(icon, info.IconPath);
        Assert.Equal(path, _service.Find("personal"));
    }

    [Fact]
    public void Whatsapp_shortcuts_are_named_whatsapp_and_found_like_claude_ones()
    {
        var path = _service.Create("Resetrix", "resetrix", null, ProfileApp.WhatsApp);
        var defaultPath = _service.Create("Default", "whatsapp-default", null, ProfileApp.WhatsApp);
        var claude = _service.Create("Personal", "personal", null);

        Assert.Equal(Path.Combine(_desktop, "WhatsApp - Resetrix.lnk"), path);
        Assert.Equal("--launch resetrix", ShellLink.Read(path).Arguments);
        Assert.Equal("--launch whatsapp-default", ShellLink.Read(defaultPath).Arguments);
        Assert.Equal(path, _service.Find("resetrix"));
        Assert.Equal(new[] { claude, defaultPath, path }.Order(), _service.FindAll().Order());
    }

    [Fact]
    public void Whatsapp_and_claude_profiles_with_the_same_name_get_separate_shortcuts()
    {
        var claude = _service.Create("Personal", "personal", null);
        var whatsApp = _service.Create("Personal", "personal-2", null, ProfileApp.WhatsApp);

        Assert.NotEqual(claude, whatsApp);
        Assert.True(File.Exists(claude));
        Assert.True(File.Exists(whatsApp));
    }

    [Fact]
    public void Renaming_a_whatsapp_profile_keeps_the_whatsapp_prefix()
    {
        _service.Create("Resetrix", "resetrix", null, ProfileApp.WhatsApp);

        var renamed = _service.Rename("resetrix", "Resetrix Sales", null, ProfileApp.WhatsApp);

        Assert.Equal(Path.Combine(_desktop, "WhatsApp - Resetrix Sales.lnk"), renamed);
        Assert.False(File.Exists(Path.Combine(_desktop, "WhatsApp - Resetrix.lnk")));
    }

    [Fact]
    public void Find_ignores_other_profiles_and_foreign_shortcuts()
    {
        _service.Create("Work", "work", null);
        ShellLink.Write(Path.Combine(_desktop, "Claude - Personal.lnk"),
            new ShellLinkInfo(@"C:\Other\tool.exe", "--launch personal", @"C:\Other", "", ""));

        Assert.Null(_service.Find("personal"));
        Assert.NotNull(_service.Find("work"));
    }

    [Fact]
    public void Create_refuses_to_overwrite_a_shortcut_it_did_not_make()
    {
        var foreign = Path.Combine(_desktop, "Claude - Personal.lnk");
        ShellLink.Write(foreign, new ShellLinkInfo(@"C:\Other\tool.exe", "", @"C:\Other", "", ""));

        Assert.Throws<ShortcutConflictException>(() => _service.Create("Personal", "personal", null));
        Assert.Equal(@"C:\Other\tool.exe", ShellLink.Read(foreign).TargetPath);
    }

    [Fact]
    public void Creating_again_refreshes_in_place()
    {
        _service.Create("Personal", "personal", null);
        _service.Create("Personal", "personal", Path.Combine(_root, "new.ico"));

        Assert.Single(Directory.GetFiles(_desktop));
        Assert.EndsWith("new.ico", ShellLink.Read(_service.Find("personal")!).IconPath);
    }

    [Fact]
    public void Rename_moves_the_shortcut_to_the_new_name()
    {
        _service.Create("Personal", "personal", null);

        var renamed = _service.Rename("personal", "Home", null);

        Assert.Equal(Path.Combine(_desktop, "Claude - Home.lnk"), renamed);
        Assert.Equal([renamed], Directory.GetFiles(_desktop));
    }

    [Fact]
    public void Rename_without_a_shortcut_creates_nothing()
    {
        Assert.Null(_service.Rename("personal", "Home", null));
        Assert.Empty(Directory.GetFiles(_desktop));
    }

    [Fact]
    public void Delete_removes_only_that_profiles_shortcut()
    {
        _service.Create("Personal", "personal", null);
        _service.Create("Work", "work", null);

        Assert.NotNull(_service.Delete("personal"));

        Assert.Equal([Path.Combine(_desktop, "Claude - Work.lnk")], Directory.GetFiles(_desktop));
        Assert.Null(_service.Delete("personal"));
    }

    // ---------- Icons ----------

    [Fact]
    public void IcoFile_rewrites_resource_ids_as_file_offsets()
    {
        // Group with two images: 16x16 (id 7) and 32x32 (id 9)
        var group = new byte[6 + 2 * 14];
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(4), 2);
        group[6] = 16; group[7] = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(6 + 12), 7);
        group[20] = 32; group[21] = 32;
        BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(20 + 12), 9);
        var images = new Dictionary<ushort, byte[]> { [7] = [1, 2, 3], [9] = [4, 5, 6, 7, 8] };

        var ico = IcoFile.FromResourceGroup(group, id => images[id]);

        Assert.Equal(6 + 2 * 16 + 3 + 5, ico.Length);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        Assert.Equal(16, ico[6]);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 8)));
        Assert.Equal(38u, BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 12)));
        Assert.Equal(32, ico[22]);
        Assert.Equal(41u, BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(22 + 12)));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], ico[38..]);
    }

    [Fact]
    public void Extracts_a_real_exe_icon_into_a_valid_ico()
    {
        var ico = Path.Combine(_root, "out", "explorer.ico");

        IconExtractor.ExtractMainIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), ico);

        var bytes = File.ReadAllBytes(ico);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        Assert.True(count > 0);
        var lastEntry = bytes.AsSpan(6 + (count - 1) * 16);
        Assert.Equal(bytes.Length,
            (int)(BinaryPrimitives.ReadUInt32LittleEndian(lastEntry[12..]) + BinaryPrimitives.ReadUInt32LittleEndian(lastEntry[8..])));
    }

    [Fact]
    public void ClaudeIcon_is_extracted_once_and_reused()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var install = new ClaudeInstall(InstallKind.Standalone, explorer, Path.GetDirectoryName(explorer)!);

        var first = ClaudeIcon.Ensure(paths, install);
        Assert.Equal(paths.ClaudeIconFile, first);
        var written = File.GetLastWriteTimeUtc(first!);

        // Even with Claude gone (e.g. mid-update), the existing copy is used.
        Assert.Equal(first, ClaudeIcon.Ensure(paths, install: null));
        Assert.Equal(written, File.GetLastWriteTimeUtc(first!));
    }

    [Fact]
    public void ClaudeIcon_returns_null_when_extraction_fails()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        var install = new ClaudeInstall(InstallKind.Standalone, Path.Combine(_root, "missing.exe"), _root);

        Assert.Null(ClaudeIcon.Ensure(paths, install));
    }

    [Fact]
    public void Msix_icon_source_is_the_exe_inside_the_package()
    {
        var install = new ClaudeInstall(InstallKind.Msix, @"C:\WA\claude-desktop.exe", @"C:\P\Claude_1_x64__pzs8sxrjxfjjc");
        Assert.Equal(@"C:\P\Claude_1_x64__pzs8sxrjxfjjc\app\Claude.exe", install.IconSourcePath);
    }

    /// <summary>Smallest byte string IcoFile reads as a PNG: signature plus IHDR width and height.</summary>
    private static byte[] FakePng(int size)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), (uint)size);
        return png;
    }

    [Fact]
    public void WhatsAppIcon_is_built_from_the_package_pngs_preferring_unplated()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));
        var package = Path.Combine(_root, "5319275A.WhatsAppDesktop_2.2645.101.0_x64__cv1g1gvanyjgm");
        var assets = Directory.CreateDirectory(Path.Combine(package, "Assets")).FullName;
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-16_altform-unplated.png"), FakePng(16));
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-16.png"), [1, 2, 3]); // plated: not used
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-32.png"), FakePng(32));
        File.WriteAllBytes(Path.Combine(assets, "AppList.targetsize-256_altform-unplated.png"), FakePng(256));

        var ico = WhatsAppIcon.Ensure(paths, new Core.WhatsApp.WhatsAppDesktop(package));

        Assert.Equal(paths.WhatsAppIconFile, ico);
        var bytes = File.ReadAllBytes(ico!);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));
        Assert.Equal([16, 32, 0], new[] { bytes[6], bytes[6 + 16], bytes[6 + 32] }); // 256 is written as 0
        Assert.Equal(paths.WhatsAppIconFile, WhatsAppIcon.Ensure(paths, null)); // reused, even once uninstalled
    }

    [Fact]
    public void WhatsAppIcon_is_null_without_whatsapp_or_its_pngs()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "Roaming"), Path.Combine(_root, "Local"), Path.Combine(_root, "home"));

        Assert.Null(WhatsAppIcon.Ensure(paths, null));
        Assert.Null(WhatsAppIcon.Ensure(paths, new Core.WhatsApp.WhatsAppDesktop(Path.Combine(_root, "empty"))));
        Assert.False(File.Exists(paths.WhatsAppIconFile));
    }
}
