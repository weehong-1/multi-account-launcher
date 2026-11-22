using System.Management;
using ClaudeMultiAccountLauncher.Core.Startup;
using ClaudeMultiAccountLauncher.Core.WhatsApp;

namespace ClaudeMultiAccountLauncher.Core.Claude;

/// <summary>A process found by image name, e.g. claude.exe (Claude Desktop or the Claude Code CLI).</summary>
public sealed record ProcessSnapshot(int ProcessId, string? ExecutablePath, string? CommandLine);

/// <summary>A running Claude Desktop main process.</summary>
/// <param name="UserDataDir">Value of <c>--user-data-dir</c>, or null for the Default profile.</param>
public sealed record RunningInstance(int ProcessId, string? UserDataDir);

public interface IProcessSource
{
    /// <summary>Processes whose image name is <paramref name="imageName"/> (e.g. <c>claude.exe</c>), any case.</summary>
    IReadOnlyList<ProcessSnapshot> GetProcesses(string imageName);
}

/// <summary>Reads executable paths and command lines through WMI.</summary>
public sealed class WmiProcessSource : IProcessSource
{
    public IReadOnlyList<ProcessSnapshot> GetProcesses(string imageName)
    {
        var escaped = imageName.Replace("\\", "\\\\").Replace("'", "\'");
        using var searcher = new ManagementObjectSearcher(
            $"SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = '{escaped}'");
        using var results = searcher.Get();

        var list = new List<ProcessSnapshot>();
        foreach (var obj in results)
        {
            using (obj)
            {
                list.Add(new ProcessSnapshot(
                    Convert.ToInt32(obj["ProcessId"]),
                    obj["ExecutablePath"] as string,
                    obj["CommandLine"] as string));
            }
        }

        return list;
    }
}

/// <summary>Finds running Claude Desktop instances (FR-7) and running WhatsApp (WhatsApp spec section 4).</summary>
public sealed class ProcessScanner(IProcessSource source)
{
    public const string ClaudeImageName = "claude.exe";

    private const string UserDataDirSwitch = "--user-data-dir=";

    /// <summary>Every Claude Desktop process of this install, children included. Never the CLI.</summary>
    public IReadOnlyList<ProcessSnapshot> DesktopProcesses(ClaudeInstall install) =>
        source.GetProcesses(ClaudeImageName).Where(p => install.OwnsExecutable(p.ExecutablePath)).ToList();

    /// <summary>One entry per running Desktop instance: main processes only (no <c>--type=</c>).</summary>
    public IReadOnlyList<RunningInstance> RunningInstances(ClaudeInstall install)
    {
        var instances = new List<RunningInstance>();
        foreach (var p in DesktopProcesses(install))
        {
            if (p.CommandLine is null)
            {
                continue; // Can't tell which profile; skip rather than guess.
            }

            var args = CommandLine.Split(p.CommandLine);
            if (args.Skip(1).Any(a => a.StartsWith("--type=", StringComparison.Ordinal)))
            {
                continue; // Electron child process (renderer, gpu, utility...)
            }

            var dataDir = args.Skip(1)
                .LastOrDefault(a => a.StartsWith(UserDataDirSwitch, StringComparison.OrdinalIgnoreCase))?[UserDataDirSwitch.Length..];

            instances.Add(new RunningInstance(p.ProcessId, string.IsNullOrEmpty(dataDir) ? null : dataDir));
        }

        return instances;
    }

    /// <summary>
    /// True when the official WhatsApp Desktop (the WhatsApp Default) is open: a
    /// <c>WhatsApp.Root.exe</c> from any version of its package that has a visible window.
    /// Not by command line: WhatsApp opens its window inside its background push process
    /// (<c>-RegisterForBGTaskServer</c>), which otherwise runs with no window (verified 2026-11-22).
    /// </summary>
    public bool IsWhatsAppDefaultRunning(IWindowProbe windows)
    {
        var candidates = source.GetProcesses(WhatsAppPackage.ExeName)
            .Where(p => WhatsAppPackage.OwnsExecutable(p.ExecutablePath))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var withWindow = windows.ProcessesWithVisibleWindow();
        return candidates.Any(p => withWindow.Contains(p.ProcessId));
    }

    /// <summary>
    /// Running WhatsApp profiles, slug to process id: launcher processes started with
    /// <c>--whatsapp &lt;slug&gt;</c>. A second launch that is still handing off (FR-W6) may show
    /// for a moment; the lowest process id is kept.
    /// </summary>
    /// <param name="launcherImageName">The launcher's exe file name, e.g. <c>claude-multi-account-launcher.exe</c>.</param>
    public IReadOnlyDictionary<string, int> RunningWhatsAppProfiles(string launcherImageName)
    {
        var running = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in source.GetProcesses(launcherImageName))
        {
            if (p.CommandLine is not null
                && StartupCommand.Parse(CommandLine.Split(p.CommandLine).Skip(1).ToList()).WhatsAppSlug is { } slug
                && (!running.TryGetValue(slug, out var known) || p.ProcessId < known))
            {
                running[slug] = p.ProcessId;
            }
        }

        return running;
    }

    /// <summary>Compares two folder paths the way Windows does (case-insensitive, trailing slash ignored).</summary>
    public static bool SameFolder(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
