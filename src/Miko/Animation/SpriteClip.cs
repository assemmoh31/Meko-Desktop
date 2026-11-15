using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Miko.Animation;

public sealed class SpriteClip : IDisposable
{
    public string Name { get; }
    public string FilePath { get; }
    public Bitmap? Strip { get; private set; }
    public int FrameCount { get; }
    public double Fps { get; }
    public int CellWidth { get; private set; }
    public int CellHeight { get; private set; }
    public double ShiftX { get; }
    public double ShiftY { get; }
    public double HeightScale { get; }
    public bool IsLooping { get; }

    public SpriteClip(
        string name,
        string filePath,
        int frameCount,
        double fps,
        double heightScale = 1.0,
        double shiftX = 0,
        double shiftY = 0,
        bool isLooping = true)
    {
        Name = name;
        FilePath = filePath;
        FrameCount = Math.Max(1, frameCount);
        Fps = fps > 0 ? fps : 24.0;
        HeightScale = heightScale;
        ShiftX = shiftX;
        ShiftY = shiftY;
        IsLooping = isLooping;
    }

    public bool Load()
    {
        if (Strip != null) return true;
        if (!File.Exists(FilePath)) return false;

        try
        {
            using var raw = new Bitmap(FilePath);
            // Convert immediately to 32bpp PARGB (premultiplied ARGB) for ultra-fast, zero-conversion blitting
            var pargb = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(pargb))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
            }

            Strip = pargb;
            CellWidth = Strip.Width / FrameCount;
            CellHeight = Strip.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Strip?.Dispose();
        Strip = null;
    }
}
