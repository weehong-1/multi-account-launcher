using System.Runtime.InteropServices;
using ClaudeMultiAccountLauncher.Core.Startup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ClaudeMultiAccountLauncher;

/// <summary>
/// Own entry point (DISABLE_XAML_GENERATED_MAIN), so a WhatsApp profile can hand off to its
/// running process before any window exists (WhatsApp spec FR-W6, verified in the Phase 1 spike).
/// </summary>
public static class Program
{
    /// <summary>Key prefix for the per-profile single-instance lock.</summary>
    public const string WhatsAppInstanceKeyPrefix = "whatsapp-";

    /// <summary>Raised on a background thread when another launch of this profile handed off to us.</summary>
    public static event Action? WhatsAppActivated;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (StartupCommand.Parse(args).WhatsAppSlug is { } slug && HandedOff(slug))
        {
            return 0;
        }

        Application.Start(init =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>
    /// True when this profile already runs in another process: the activation went there (which
    /// brings its window to the front) and this process should exit without a window.
    /// </summary>
    private static bool HandedOff(string slug)
    {
        var keeper = AppInstance.FindOrRegisterForKey(WhatsAppInstanceKeyPrefix + slug);
        if (keeper.IsCurrent)
        {
            AppInstance.GetCurrent().Activated += (_, _) => WhatsAppActivated?.Invoke();
            return false;
        }

        // We were started by the user, so we may let the running process take the foreground.
        AllowSetForegroundWindow((uint)keeper.ProcessId);
        RedirectAndWait(keeper, AppInstance.GetCurrent().GetActivatedEventArgs());
        return true;
    }

    private static void RedirectAndWait(AppInstance keeper, AppActivationArguments activation)
    {
        // Windows App SDK pattern: redirect off the STA thread and pump COM while waiting.
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try
            {
                keeper.RedirectActivationToAsync(activation).AsTask().Wait();
            }
            catch (Exception)
            {
                // The running process went away mid-redirect; nothing useful to show from here.
            }
            finally
            {
                SetEvent(done);
            }
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        CloseHandle(done);
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetEvent(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
}
