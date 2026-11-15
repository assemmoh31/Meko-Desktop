using System;
using Miko.Win32;

namespace Miko.Physics;

public sealed class Ledge
{
    public IntPtr Window { get; set; }
    public string Title { get; set; } = string.Empty;
    public RECT Bounds { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public int Y { get; set; }
    public bool IsFloor { get; set; }

    public int Width => Right - Left;

    public bool ContainsX(int x, int margin = 8)
    {
        return x >= Left - margin && x <= Right + margin;
    }
}
