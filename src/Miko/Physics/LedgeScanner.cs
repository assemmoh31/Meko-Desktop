using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Miko.Win32;

namespace Miko.Physics;

public static class LedgeScanner
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "tooltips_class32",
        "#32768",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "Shell_InputSwitchTopLevelWindow",
        "NotifyIconOverflowWindow"
    };

    public static List<Ledge> ScanActiveLedges(HashSet<IntPtr> ignoredHwnds)
    {
        var foundLedges = new List<Ledge>();
        var aboveWindows = new List<RECT>();

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (ignoredHwnds.Contains(hWnd)) return true;
            if (!NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;

            // Check if window is cloaked (e.g. on another virtual desktop)
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            // Check class name
            var sbClass = new StringBuilder(128);
            NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
            string className = sbClass.ToString();
            if (IgnoredClasses.Contains(className)) return true;

            // Get window rect
            RECT rect;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) != 0)
            {
                if (!NativeMethods.GetWindowRect(hWnd, out rect)) return true;
            }

            if (rect.Width < 80 || rect.Height < 60) return true;

            // Check window title
            var sbTitle = new StringBuilder(256);
            NativeMethods.GetWindowText(hWnd, sbTitle, sbTitle.Capacity);
            string title = sbTitle.ToString();

            // Calculate unoccluded spans for the top ledge
            var openSpans = CalculateOpenSpans(rect.Left, rect.Right, rect.Top, aboveWindows);
            foreach (var span in openSpans)
            {
                if (span.Right - span.Left >= 32)
                {
                    foundLedges.Add(new Ledge
                    {
                        Window = hWnd,
                        Title = title,
                        Bounds = rect,
                        Left = span.Left,
                        Right = span.Right,
                        Y = rect.Top,
                        IsFloor = false
                    });
                }
            }

            aboveWindows.Add(rect);
            return true;
        }, IntPtr.Zero);

        // Add floor ledge for each screen (working area bottom = taskbar top)
        foreach (var screen in Screen.AllScreens)
        {
            foundLedges.Add(new Ledge
            {
                Window = IntPtr.Zero,
                Title = "Desktop Floor",
                Bounds = new RECT(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Right, screen.WorkingArea.Bottom),
                Left = screen.WorkingArea.Left,
                Right = screen.WorkingArea.Right,
                Y = screen.WorkingArea.Bottom,
                IsFloor = true
            });
        }

        return foundLedges;
    }

    private static List<(int Left, int Right)> CalculateOpenSpans(int left, int right, int y, List<RECT> aboveWindows)
    {
        var spans = new List<(int Left, int Right)> { (left, right) };

        foreach (var cover in aboveWindows)
        {
            if (y < cover.Top || y >= cover.Bottom) continue;

            var nextSpans = new List<(int Left, int Right)>();
            foreach (var span in spans)
            {
                if (cover.Right <= span.Left || cover.Left >= span.Right)
                {
                    nextSpans.Add(span);
                    continue;
                }

                if (cover.Left > span.Left)
                {
                    nextSpans.Add((span.Left, cover.Left));
                }
                if (cover.Right < span.Right)
                {
                    nextSpans.Add((cover.Right, span.Right));
                }
            }
            spans = nextSpans;
        }

        return spans;
    }

    public static Ledge? FindBestLedgeUnder(IEnumerable<Ledge> ledges, int hipX, int hipY, int maxReach = 1200)
    {
        Ledge? best = null;
        int minDistance = int.MaxValue;

        foreach (var ledge in ledges)
        {
            if (!ledge.ContainsX(hipX, 16)) continue;

            int drop = ledge.Y - hipY;
            // Ledge must be below hip (with small negative slack if hip is just a few px below top)
            if (drop >= -12 && drop <= maxReach)
            {
                int dist = Math.Abs(drop);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    best = ledge;
                }
            }
        }

        return best;
    }

    public static bool TryGetWindowBounds(IntPtr hWnd, out RECT bounds)
    {
        bounds = default;
        if (!NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<RECT>()) == 0)
        {
            return true;
        }

        return NativeMethods.GetWindowRect(hWnd, out bounds);
    }
}
