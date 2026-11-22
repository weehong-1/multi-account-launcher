using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeMultiAccountLauncher.Core.WhatsApp;

/// <summary>Seam so window checks can be tested without real windows.</summary>
public interface IWindowProbe
{
    /// <summary>Process ids that own at least one window a user would see as "the app is open".</summary>
    IReadOnlySet<int> ProcessesWithVisibleWindow();
}

/// <summary>
/// Top-level windows that are visible, not cloaked, not tool windows and have a title.
/// WhatsApp Desktop shows its window inside its background push process (verified 2026-11-22),
/// so the window, not the command line, tells whether it's open (WhatsApp spec section 4).
/// </summary>
public sealed class SystemWindowProbe : IWindowProbe
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int DwmwaCloaked = 14;

    public IReadOnlySet<int> ProcessesWithVisibleWindow()
    {
        var pids = new HashSet<int>();
        EnumWindows((hwnd, _) =>
        {
            if (IsUserVisible(hwnd))
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                pids.Add((int)pid);
            }

            return true;
        }, IntPtr.Zero);
        return pids;
    }

    private static bool IsUserVisible(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        if ((GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0)
        {
            return false;
        }

        return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
