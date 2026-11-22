using System.Globalization;
using System.Runtime.InteropServices;

namespace ClaudeMultiAccountLauncher;

/// <summary>
/// Unread count on a window's taskbar button (WhatsApp spec FR-W3), as an overlay icon through
/// ITaskbarList3. Unpackaged apps can't use taskbar badges, so the number is drawn here: a green
/// circle with white digits from a 3x5 pixel font, scaled to the small icon size.
/// </summary>
public sealed class TaskbarBadge(IntPtr hwnd)
{
    private const int SmCxSmIcon = 49;
    private const int Background = unchecked((int)0xFF1DAA61); // opaque green, BGRA as one int
    private const int Foreground = unchecked((int)0xFFFFFFFF);

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["111", "101", "101", "101", "111"],
        ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"],
        ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"],
        ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"],
        ['7'] = ["111", "001", "001", "001", "001"],
        ['8'] = ["111", "101", "111", "101", "111"],
        ['9'] = ["111", "101", "111", "001", "111"],
        ['+'] = ["000", "010", "111", "010", "000"],
    };

    private ITaskbarList3? _taskbar;
    private int _shown;

    /// <summary>Shows <paramref name="count"/>, or clears the overlay for 0. Call on the UI thread.</summary>
    public void Show(int count)
    {
        if (count == _shown)
        {
            return;
        }

        try
        {
            _taskbar ??= CreateTaskbarList();
            if (count <= 0)
            {
                _taskbar.SetOverlayIcon(hwnd, IntPtr.Zero, null);
            }
            else
            {
                var icon = CreateIcon(count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture));
                try
                {
                    var text = count == 1 ? "1 unread chat" : $"{count} unread chats";
                    _taskbar.SetOverlayIcon(hwnd, icon, text);
                }
                finally
                {
                    DestroyIcon(icon); // the taskbar keeps its own copy
                }
            }

            _shown = count;
        }
        catch (COMException)
        {
            // No taskbar (e.g. Explorer restarting): the count shows again on the next title change.
            _taskbar = null;
        }
    }

    private static ITaskbarList3 CreateTaskbarList()
    {
        var list = (ITaskbarList3)new TaskbarListClass();
        list.HrInit();
        return list;
    }

    private IntPtr CreateIcon(string text)
    {
        var size = Math.Max(16, GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForWindow(hwnd)));
        var pixels = new int[size * size];

        // Circle
        var r = size / 2.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5 - r;
                var dy = y + 0.5 - r;
                if ((dx * dx) + (dy * dy) <= r * r)
                {
                    pixels[(y * size) + x] = Background;
                }
            }
        }

        // Digits: the largest whole scale that fits inside the circle with a small margin
        var inner = size - (2 * Math.Max(1, size / 8));
        var units = (text.Length * 4) - 1;
        var scale = Math.Max(1, Math.Min(inner / units, inner / 5));
        var left = (size - (units * scale)) / 2;
        var top = (size - (5 * scale)) / 2;
        for (var i = 0; i < text.Length; i++)
        {
            var glyph = Glyphs[text[i]];
            for (var gy = 0; gy < 5; gy++)
            {
                for (var gx = 0; gx < 3; gx++)
                {
                    if (glyph[gy][gx] != '1')
                    {
                        continue;
                    }

                    for (var sy = 0; sy < scale; sy++)
                    {
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var px = left + (((i * 4) + gx) * scale) + sx;
                            var py = top + (gy * scale) + sy;
                            if (px >= 0 && px < size && py >= 0 && py < size)
                            {
                                pixels[(py * size) + px] = Foreground;
                            }
                        }
                    }
                }
            }
        }

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        try
        {
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(color);
            DeleteObject(mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarListClass;

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

        // ITaskbarList3, in vtable order up to SetOverlayIcon
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr tab, IntPtr mdi);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr insertBefore);
        void SetTabActive(IntPtr tab, IntPtr mdi, uint flags);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
