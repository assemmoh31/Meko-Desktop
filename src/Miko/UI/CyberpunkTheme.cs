using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;

namespace Miko.UI;

public static class CyberpunkTheme
{
    public static readonly Color Background = Color.FromArgb(235, 12, 16, 23);
    public static readonly Color CardBackground = Color.FromArgb(245, 18, 24, 38);
    public static readonly Color NeonCyan = Color.FromArgb(0, 240, 255);
    public static readonly Color NeonPink = Color.FromArgb(255, 42, 109);
    public static readonly Color NeonGreen = Color.FromArgb(5, 217, 232);
    public static readonly Color TextPrimary = Color.FromArgb(245, 248, 252);
    public static readonly Color TextSecondary = Color.FromArgb(144, 160, 180);
    public static readonly Color Border = Color.FromArgb(40, 60, 85);
    public static readonly Color PromptColor = Color.FromArgb(165, 205, 235);
    public static readonly Color DimDivider = Color.FromArgb(35, 55, 80);
    public static readonly Color StatusAmber = Color.FromArgb(255, 175, 20);
    public static readonly Color StatusGreen = Color.FromArgb(0, 240, 160);

    private static readonly PrivateFontCollection FontCollection = new();
    public static Font MonoFont { get; private set; }
    public static Font TitleFont { get; private set; }
    public static Font HeaderFont { get; private set; }
    public static Font PromptFont { get; private set; }

    static CyberpunkTheme()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fontPath = Path.Combine(baseDir, "..", "..", "..", "assets", "fonts", "ShareTechMono-Regular.ttf");
        if (!File.Exists(fontPath))
        {
            fontPath = Path.Combine(baseDir, "assets", "fonts", "ShareTechMono-Regular.ttf");
        }

        if (File.Exists(fontPath))
        {
            try
            {
                FontCollection.AddFontFile(fontPath);
                var fontFamily = FontCollection.Families[0];
                MonoFont = new Font(fontFamily, 10f, FontStyle.Regular);
                TitleFont = new Font(fontFamily, 11f, FontStyle.Bold);
                HeaderFont = new Font(fontFamily, 8f, FontStyle.Bold);
                PromptFont = new Font(fontFamily, 9.5f, FontStyle.Regular);
                return;
            }
            catch
            {
                // Fallback to Consolas
            }
        }

        MonoFont = new Font("Consolas", 10f, FontStyle.Regular);
        TitleFont = new Font("Consolas", 11f, FontStyle.Bold);
        HeaderFont = new Font("Consolas", 8f, FontStyle.Bold);
        PromptFont = new Font("Consolas", 9.5f, FontStyle.Regular);
    }
}
