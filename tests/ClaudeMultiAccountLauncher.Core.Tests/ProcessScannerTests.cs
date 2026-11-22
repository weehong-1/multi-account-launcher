using ClaudeMultiAccountLauncher.Core.Claude;
using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Tests;

public class ProcessScannerTests
{
    private const string PackageDir = @"C:\Program Files\WindowsApps\Claude_6.6395.0.0_x64__pzs8sxrjxfjjc";
    private const string DesktopExe = PackageDir + @"\app\Claude.exe";
    private const string Alias = @"C:\Users\V\AppData\Local\Microsoft\WindowsApps\claude-desktop.exe";
    private const string CliExe = @"C:\Users\V\AppData\Local\Claude-Data\claude_pzs8sxrjxfjjc\claude-code\2.1.326\abc\claude.exe";

    private static readonly ClaudeInstall Msix = new(InstallKind.Msix, Alias, PackageDir);

    private sealed class FakeSource(params ProcessSnapshot[] processes) : IProcessSource
    {
        public IReadOnlyList<ProcessSnapshot> GetProcesses(string imageName) =>
            processes.Where(p => string.Equals(Path.GetFileName(p.ExecutablePath), imageName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // Real command lines captured in the Phase 1 spike
    private static readonly ProcessSnapshot DefaultMain = new(100, DesktopExe, $"\"{DesktopExe}\" ");
    private static readonly ProcessSnapshot DefaultRenderer = new(101, DesktopExe, $"\"{DesktopExe}\" --type=renderer --user-data-dir=\"C:\\x\"");
    private static readonly ProcessSnapshot ProfileMain = new(200, DesktopExe, $"\"{Alias}\" --user-data-dir=C:\\Users\\V\\AppData\\Roaming\\Claude-personal");
    private static readonly ProcessSnapshot ProfileGpu = new(201, DesktopExe, $"\"{DesktopExe}\" --type=gpu-process --user-data-dir=C:\\Users\\V\\AppData\\Roaming\\Claude-personal");
    private static readonly ProcessSnapshot Cli = new(300, CliExe, $"\"{CliExe}\" --resume");

    [Fact]
    public void Desktop_processes_exclude_the_Claude_Code_CLI()
    {
        var scanner = new ProcessScanner(new FakeSource(DefaultMain, DefaultRenderer, ProfileMain, ProfileGpu, Cli));

        var ids = scanner.DesktopProcesses(Msix).Select(p => p.ProcessId);

        Assert.Equal([100, 101, 200, 201], ids);
    }

    [Fact]
    public void Running_instances_are_main_processes_keyed_by_user_data_dir()
    {
        var scanner = new ProcessScanner(new FakeSource(DefaultMain, DefaultRenderer, ProfileMain, ProfileGpu, Cli));

        var running = scanner.RunningInstances(Msix);

        Assert.Equal(
            [new RunningInstance(100, null), new RunningInstance(200, @"C:\Users\V\AppData\Roaming\Claude-personal")],
            running);
    }

    [Fact]
    public void Quoted_whole_argument_with_spaces_is_parsed()
    {
        // What .NET's ProcessStartInfo.ArgumentList produces when the path has a space
        var p = new ProcessSnapshot(1, DesktopExe, $"\"{Alias}\" \"--user-data-dir=C:\\Users\\Ann Lee\\AppData\\Roaming\\Claude-work\"");

        var running = new ProcessScanner(new FakeSource(p)).RunningInstances(Msix);

        Assert.Equal(@"C:\Users\Ann Lee\AppData\Roaming\Claude-work", Assert.Single(running).UserDataDir);
    }

    [Fact]
    public void Process_from_previous_package_version_still_counts_as_desktop()
    {
        var oldExe = @"C:\Program Files\WindowsApps\Claude_6.6000.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe";
        var scanner = new ProcessScanner(new FakeSource(new ProcessSnapshot(1, oldExe, $"\"{oldExe}\"")));

        Assert.Single(scanner.DesktopProcesses(Msix));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\ClaudeHelper_1.0.0.0_x64__other123\app\claude.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_6.6395.0.0_x64__someoneelse\app\claude.exe")]
    [InlineData(@"C:\tools\claude.exe")]
    [InlineData(null)]
    public void Unrelated_claude_exe_is_ignored(string? exe) =>
        Assert.False(Msix.OwnsExecutable(exe));

    [Fact]
    public void Standalone_install_matches_its_versioned_app_folder()
    {
        var standalone = new ClaudeInstall(InstallKind.Standalone, @"C:\U\AppData\Local\AnthropicClaude\claude.exe", @"C:\U\AppData\Local\AnthropicClaude");

        Assert.True(standalone.OwnsExecutable(@"C:\U\AppData\Local\AnthropicClaude\app-0.9.3\claude.exe"));
        Assert.False(standalone.OwnsExecutable(@"C:\U\AppData\Local\AnthropicClaudeOther\claude.exe"));
    }

    private const string WhatsAppExe = @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2645.101.0_x64__cv1g1gvanyjgm\WhatsApp.Root.exe";

    private sealed class FakeWindows(params int[] pids) : IWindowProbe
    {
        public IReadOnlySet<int> ProcessesWithVisibleWindow() => pids.ToHashSet();
    }

    // Real command line from 2026-11-22: WhatsApp's window lives in its push process
    private static readonly ProcessSnapshot WhatsAppPush =
        new(36328, WhatsAppExe, $"\"{WhatsAppExe}\" -RegisterForBGTaskServer /nowindow /pushnotification -Embedding");

    [Fact]
    public void WhatsApp_default_runs_when_its_process_has_a_visible_window() =>
        Assert.True(new ProcessScanner(new FakeSource(WhatsAppPush)).IsWhatsAppDefaultRunning(new FakeWindows(36328)));

    [Fact]
    public void WhatsApp_default_without_a_visible_window_is_not_running()
    {
        Assert.False(new ProcessScanner(new FakeSource(WhatsAppPush)).IsWhatsAppDefaultRunning(new FakeWindows()));
        Assert.False(new ProcessScanner(new FakeSource()).IsWhatsAppDefaultRunning(new FakeWindows(36328)));
    }

    [Fact]
    public void A_window_of_another_process_does_not_count()
    {
        var elsewhere = new ProcessSnapshot(7, @"C:\tools\WhatsApp.Root.exe", null);

        Assert.False(new ProcessScanner(new FakeSource(WhatsAppPush, elsewhere)).IsWhatsAppDefaultRunning(new FakeWindows(7, 99)));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2600.1.0_x64__cv1g1gvanyjgm\app\WhatsApp.Root.exe", true)]
    [InlineData(@"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2645.101.0_x64__someoneelse\WhatsApp.Root.exe", false)]
    [InlineData(@"C:\tools\WhatsApp.Root.exe", false)]
    [InlineData(@"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2645.101.0_x64__cv1g1gvanyjgm\Other.exe", false)]
    [InlineData(null, false)]
    public void WhatsApp_exe_matches_any_version_of_its_package_only(string? exe, bool expected) =>
        Assert.Equal(expected, WhatsAppPackage.OwnsExecutable(exe));

    [Fact]
    public void Running_whatsapp_profiles_come_from_launcher_command_lines()
    {
        const string launcher = @"C:\U\AppData\Local\Programs\claude-multi-account-launcher\claude-multi-account-launcher.exe";
        var scanner = new ProcessScanner(new FakeSource(
            new ProcessSnapshot(1, launcher, $"\"{launcher}\""),                        // the launcher window
            new ProcessSnapshot(2, launcher, $"\"{launcher}\" --whatsapp resetrix"),
            new ProcessSnapshot(3, launcher, $"\"{launcher}\" --whatsapp=Personal-2"),
            new ProcessSnapshot(4, launcher, $"\"{launcher}\" --launch work"),           // a shortcut, not a window
            new ProcessSnapshot(5, launcher, null)));

        var running = scanner.RunningWhatsAppProfiles("claude-multi-account-launcher.exe");
        Assert.Equal(["personal-2", "resetrix"], running.Keys.Order());
        Assert.Equal(2, running["resetrix"]);
        Assert.Equal(3, running["personal-2"]);
    }

    [Theory]
    [InlineData(@"C:\A\Claude-x", @"c:\a\claude-x\", true)]
    [InlineData(@"C:\A\Claude-x", @"C:\A\Claude-xy", false)]
    public void SameFolder_ignores_case_and_trailing_slash(string a, string b, bool expected) =>
        Assert.Equal(expected, ProcessScanner.SameFolder(a, b));
}
