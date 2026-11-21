using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using System.Windows.Forms;
using Miko.Win32;

namespace Miko.UI;

public sealed class SpeechBubbleForm : Form
{
    private readonly System.Windows.Forms.Timer _dismissTimer;
    private readonly System.Windows.Forms.Timer _typewriterTimer;
    private readonly System.Windows.Forms.Timer _thinkingTimer;

    private string? _userPrompt = null;
    private string _fullReply = string.Empty;
    private int _typewriterIndex = 0;
    private bool _isThinking = false;
    private int _thinkingCycle = 0;
    private bool _isTailAtTop = false;

    private const int TailWidth = 18;
    private const int TailHeight = 12;
    private const int CornerRadius = 10;
    private const int BaseBubbleWidth = 380;

    public SpeechBubbleForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        TopMost = true;
        BackColor = Color.FromArgb(10, 14, 22);
        Cursor = Cursors.Hand;
        Size = new Size(BaseBubbleWidth, 120);

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        _dismissTimer = new System.Windows.Forms.Timer { Interval = 9000 };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            Hide();
        };

        _typewriterTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _typewriterTimer.Tick += (s, e) =>
        {
            if (_typewriterIndex < _fullReply.Length)
            {
                _typewriterIndex++;
                Invalidate();
            }
            else
            {
                _typewriterTimer.Stop();
                _dismissTimer.Start();
                Invalidate();
            }
        };

        _thinkingTimer = new System.Windows.Forms.Timer { Interval = 220 };
        _thinkingTimer.Tick += (s, e) =>
        {
            _thinkingCycle = (_thinkingCycle + 1) % 4;
            Invalidate();
        };

        Click += (s, e) => Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    /// <summary>
    /// Displays a full conversation dialogue: the user's question followed by Miko's reply.
    /// </summary>
    public void ShowDialogue(string? userPrompt, string mikoReply, Point companionAnchor, int companionWidth = 320, int? charIntervalMs = null)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ShowDialogue(userPrompt, mikoReply, companionAnchor, companionWidth, charIntervalMs)));
            return;
        }

        _dismissTimer.Stop();
        _thinkingTimer.Stop();
        _typewriterTimer.Stop();

        _isThinking = false;
        _userPrompt = string.IsNullOrWhiteSpace(userPrompt) ? null : userPrompt.Trim();
        _fullReply = mikoReply;
        _typewriterIndex = 0;

        _typewriterTimer.Interval = (charIntervalMs.HasValue && charIntervalMs.Value > 0)
            ? charIntervalMs.Value
            : 25;

        RecalculateLayout(companionAnchor, companionWidth);

        if (!Visible)
        {
            Show();
        }

        _typewriterTimer.Start();
        Invalidate();
    }

    /// <summary>
    /// Displays the thinking state while Miko and Gemini process the question.
    /// </summary>
    public void ShowThinking(string userPrompt, Point companionAnchor, int companionWidth = 320)
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => ShowThinking(userPrompt, companionAnchor, companionWidth)));
            return;
        }

        _dismissTimer.Stop();
        _typewriterTimer.Stop();

        _isThinking = true;
        _userPrompt = string.IsNullOrWhiteSpace(userPrompt) ? null : userPrompt.Trim();
        _fullReply = string.Empty;
        _typewriterIndex = 0;
        _thinkingCycle = 0;

        RecalculateLayout(companionAnchor, companionWidth);

        if (!Visible)
        {
            Show();
        }

        _thinkingTimer.Start();
        Invalidate();
    }

    /// <summary>
    /// Standard single-message API for greetings and system notifications.
    /// </summary>
    public void ShowMessage(string text, Point companionAnchor, int companionWidth = 320, int? charIntervalMs = null)
    {
        ShowDialogue(userPrompt: null, mikoReply: text, companionAnchor, companionWidth, charIntervalMs);
    }

    /// <summary>
    /// Instantly finalizes typewriter text and begins dismiss countdown.
    /// </summary>
    public void CompleteTypewriter()
    {
        if (InvokeRequired)
        {
            BeginInvoke((Action)CompleteTypewriter);
            return;
        }

        if (_typewriterTimer.Enabled)
        {
            _typewriterTimer.Stop();
            _typewriterIndex = _fullReply.Length;
            _dismissTimer.Start();
            Invalidate();
        }
    }

    private void RecalculateLayout(Point companionAnchor, int companionWidth)
    {
        int availableContentWidth = BaseBubbleWidth - 36;

        int headerH = 24;
        int promptH = 0;
        if (!string.IsNullOrEmpty(_userPrompt))
        {
            using var g = CreateGraphics();
            string promptHeader = "> you: ";
            var headerSize = g.MeasureString(promptHeader, CyberpunkTheme.PromptFont);
            int promptFirstW = BaseBubbleWidth - 24 - (int)headerSize.Width - 16;
            int promptSubsequentW = BaseBubbleWidth - 40;
            var promptLines = WrapTextLines(g, CyberpunkTheme.PromptFont, _userPrompt, promptFirstW, promptSubsequentW);
            int pLineHeight = (int)g.MeasureString("Ag", CyberpunkTheme.PromptFont).Height + 2;
            promptH = Math.Max(18, promptLines.Count * pLineHeight);
        }

        int replyH = 0;
        using (var g = CreateGraphics())
        {
            string replySample = _isThinking ? "MIKO: [NEURAL PROCESSING...] █" : ("MIKO: " + _fullReply);
            var replySize = g.MeasureString(replySample, CyberpunkTheme.MonoFont, availableContentWidth);
            replyH = Math.Max(24, (int)replySize.Height + 10);
        }

        int calculatedHeight = headerH + (promptH > 0 ? promptH + 12 : 0) + replyH + 30 + TailHeight;
        calculatedHeight = Math.Clamp(calculatedHeight, 90, 320);

        Size = new Size(BaseBubbleWidth, calculatedHeight);

        UpdatePosition(companionAnchor, companionWidth);
    }

    public void UpdatePosition(Point companionAnchor, int companionWidth = 320)
    {
        var screen = Screen.FromPoint(companionAnchor);
        int bubbleX = companionAnchor.X + (companionWidth / 2) - (Width / 2);
        int bubbleY = companionAnchor.Y - Height - 8;

        if (bubbleY < screen.WorkingArea.Top + 10)
        {
            // Position below Miko if crowded near top of screen
            bubbleY = companionAnchor.Y + 220;
            _isTailAtTop = true;
        }
        else
        {
            _isTailAtTop = false;
        }

        bubbleX = Math.Clamp(bubbleX, screen.WorkingArea.Left + 12, screen.WorkingArea.Right - Width - 12);
        Location = new Point(bubbleX, bubbleY);

        // Update form region to match custom bubble shape
        using var path = CreateBubblePath(Width, Height, _isTailAtTop);
        Region?.Dispose();
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var path = CreateBubblePath(Width, Height, _isTailAtTop);

        // 1. Draw Glassmorphism Obsidian Card Gradient Fill
        using (var brush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(0, Height),
            Color.FromArgb(18, 26, 42),
            Color.FromArgb(8, 12, 20)))
        {
            g.FillPath(brush, path);
        }

        // 2. Draw Subtle Holographic Cyber Scanlines
        int cardTop = _isTailAtTop ? TailHeight : 0;
        int cardBottom = _isTailAtTop ? Height : Height - TailHeight;
        using (var scanlinePen = new Pen(Color.FromArgb(8, 255, 255, 255), 1f))
        {
            for (int y = cardTop + 2; y < cardBottom - 2; y += 4)
            {
                g.DrawLine(scanlinePen, 10, y, Width - 10, y);
            }
        }

        // 3. Draw Neon Cyan Borders (Soft Outer Glow + Crisp Inner Stroke)
        using (var glowPen = new Pen(Color.FromArgb(70, 0, 240, 255), 3f))
        {
            g.DrawPath(glowPen, path);
        }
        using (var borderPen = new Pen(CyberpunkTheme.NeonCyan, 1.5f))
        {
            g.DrawPath(borderPen, path);
        }

        // 4. Draw Hot Pink Corner Tech Brackets
        int rectY = _isTailAtTop ? TailHeight : 0;
        using (var pinkPen = new Pen(CyberpunkTheme.NeonPink, 2.5f))
        {
            // Top-Left bracket
            g.DrawLine(pinkPen, 6, rectY + 3, 20, rectY + 3);
            g.DrawLine(pinkPen, 3, rectY + 6, 3, rectY + 20);

            // Top-Right bracket
            g.DrawLine(pinkPen, Width - 20, rectY + 3, Width - 6, rectY + 3);
            g.DrawLine(pinkPen, Width - 3, rectY + 6, Width - 3, rectY + 20);
        }

        // 5. Draw Header Bar (Status Dot, Direct Neural Link Label, State Badge)
        int headerY = rectY + 10;
        Color statusColor = _isThinking ? CyberpunkTheme.StatusAmber :
            (_typewriterTimer.Enabled ? CyberpunkTheme.StatusGreen : CyberpunkTheme.NeonCyan);

        using (var dotBrush = new SolidBrush(statusColor))
        {
            g.FillEllipse(dotBrush, 16, headerY + 4, 7, 7);
        }

        using (var titleBrush = new SolidBrush(CyberpunkTheme.NeonCyan))
        {
            g.DrawString("MIKO // NEURAL SYNC", CyberpunkTheme.HeaderFont, titleBrush, 28, headerY);
        }

        string badgeText = _isThinking ? "[COMPUTING]" : (_typewriterTimer.Enabled ? "[VOICE ACTIVE]" : "[LINK ONLINE]");
        using (var badgeBrush = new SolidBrush(statusColor))
        {
            var badgeSize = g.MeasureString(badgeText, CyberpunkTheme.HeaderFont);
            g.DrawString(badgeText, CyberpunkTheme.HeaderFont, badgeBrush, Width - badgeSize.Width - 16, headerY);
        }

        // Header divider
        using (var dividerPen = new Pen(CyberpunkTheme.DimDivider, 1f))
        {
            g.DrawLine(dividerPen, 16, headerY + 16, Width - 16, headerY + 16);
        }

        // 6. Draw User Prompt Section (if present)
        int currentY = headerY + 24;
        if (!string.IsNullOrEmpty(_userPrompt))
        {
            // Left pink vertical accent indicator
            using (var accentBrush = new SolidBrush(CyberpunkTheme.NeonPink))
            {
                g.FillRectangle(accentBrush, 16, currentY + 1, 3, 14);
            }

            // Draw "> you:" in neon cyan and prompt text in soft cyan
            string promptHeader = "> you: ";
            var headerSize = g.MeasureString(promptHeader, CyberpunkTheme.PromptFont);

            using (var youBrush = new SolidBrush(CyberpunkTheme.NeonCyan))
            {
                g.DrawString(promptHeader, CyberpunkTheme.PromptFont, youBrush, 24, currentY);
            }

            int promptFirstW = Width - 24 - (int)headerSize.Width - 16;
            int promptSubsequentW = Width - 40;
            var promptLines = WrapTextLines(g, CyberpunkTheme.PromptFont, _userPrompt, promptFirstW, promptSubsequentW);

            int pLineHeight = (int)g.MeasureString("Ag", CyberpunkTheme.PromptFont).Height + 2;
            using (var textBrush = new SolidBrush(CyberpunkTheme.PromptColor))
            {
                for (int i = 0; i < promptLines.Count; i++)
                {
                    int x = (i == 0) ? 24 + (int)headerSize.Width : 24;
                    g.DrawString(promptLines[i], CyberpunkTheme.PromptFont, textBrush, x, currentY);
                    currentY += pLineHeight;
                }
            }
            currentY += 6;

            // Subtle dashed divider line separating prompt from Miko's reply
            using (var dashedPen = new Pen(CyberpunkTheme.DimDivider, 1f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(dashedPen, 16, currentY, Width - 16, currentY);
            }
            currentY += 8;
        }

        // 7. Draw Miko Response Section
        string mikoTag = "MIKO: ";
        var tagSize = g.MeasureString(mikoTag, CyberpunkTheme.TitleFont);

        using (var tagBrush = new SolidBrush(CyberpunkTheme.NeonPink))
        {
            g.DrawString(mikoTag, CyberpunkTheme.TitleFont, tagBrush, 16, currentY);
        }

        int replyX = 16 + (int)tagSize.Width + 4;
        int replyW = Width - replyX - 16;

        if (_isThinking)
        {
            string[] dots = { "   ", ".  ", ".. ", "..." };
            string thinkingText = "SYNAPSE PROCESSING" + dots[_thinkingCycle] + " █";
            using var thinkBrush = new SolidBrush(CyberpunkTheme.StatusAmber);
            g.DrawString(thinkingText, CyberpunkTheme.MonoFont, thinkBrush, replyX, currentY);
        }
        else
        {
            string displayText;
            if (_typewriterIndex < _fullReply.Length)
            {
                displayText = _fullReply.Substring(0, _typewriterIndex) + "█";
            }
            else
            {
                displayText = _fullReply;
            }

            // Wrap text with line-0 indent so line 0 starts after 'MIKO: ' and line 1+ starts at left margin
            int firstLineWidth = replyW;
            int subsequentLineWidth = Width - 32;

            var wrappedLines = WrapTextLines(g, CyberpunkTheme.MonoFont, displayText, firstLineWidth, subsequentLineWidth);

            int lineY = currentY;
            int lineHeight = (int)g.MeasureString("Ag", CyberpunkTheme.MonoFont).Height + 2;

            for (int i = 0; i < wrappedLines.Count; i++)
            {
                string line = wrappedLines[i];
                int x = (i == 0) ? replyX : 16;

                // Colorize the blinking block cursor if present at end of string
                if (line.EndsWith("█"))
                {
                    string textPart = line.Substring(0, line.Length - 1);
                    using var textBrush = new SolidBrush(CyberpunkTheme.TextPrimary);
                    g.DrawString(textPart, CyberpunkTheme.MonoFont, textBrush, x, lineY);

                    var textSize = g.MeasureString(textPart, CyberpunkTheme.MonoFont);
                    using var cursorBrush = new SolidBrush(CyberpunkTheme.NeonCyan);
                    g.DrawString("█", CyberpunkTheme.MonoFont, cursorBrush, x + textSize.Width - 6, lineY);
                }
                else
                {
                    using var textBrush = new SolidBrush(CyberpunkTheme.TextPrimary);
                    g.DrawString(line, CyberpunkTheme.MonoFont, textBrush, x, lineY);
                }

                lineY += lineHeight;
            }
        }
    }

    private static List<string> WrapTextLines(Graphics g, Font font, string text, int firstLineWidth, int subsequentLineWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        string[] words = text.Split(' ');
        var currentLine = new StringBuilder();
        int currentLimit = firstLineWidth;

        foreach (var word in words)
        {
            string test = currentLine.Length == 0 ? word : currentLine + " " + word;
            var size = g.MeasureString(test, font);

            if (size.Width > currentLimit && currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(word);
                currentLimit = subsequentLineWidth;
            }
            else
            {
                if (currentLine.Length > 0) currentLine.Append(' ');
                currentLine.Append(word);
            }
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines;
    }

    private static GraphicsPath CreateBubblePath(int w, int h, bool tailAtTop)
    {
        var path = new GraphicsPath();
        int r = CornerRadius;
        int d = r * 2;
        int th = TailHeight;
        int tw = TailWidth;
        int tailX = w / 2;

        if (!tailAtTop)
        {
            int bottom = h - th - 1;
            // Top-Left corner
            path.AddArc(0, 0, d, d, 180, 90);
            // Top edge
            path.AddLine(r, 0, w - 1 - r, 0);
            // Top-Right corner
            path.AddArc(w - 1 - d, 0, d, d, 270, 90);
            // Right edge
            path.AddLine(w - 1, r, w - 1, bottom - r);
            // Bottom-Right corner
            path.AddArc(w - 1 - d, bottom - d, d, d, 0, 90);
            // Bottom edge right of tail
            path.AddLine(w - 1 - r, bottom, tailX + tw / 2, bottom);
            // Tail pointing down to Miko
            path.AddLine(tailX + tw / 2, bottom, tailX, h - 1);
            path.AddLine(tailX, h - 1, tailX - tw / 2, bottom);
            // Bottom edge left of tail
            path.AddLine(tailX - tw / 2, bottom, r, bottom);
            // Bottom-Left corner
            path.AddArc(0, bottom - d, d, d, 90, 90);
            // Left edge
            path.AddLine(0, bottom - r, 0, r);
        }
        else
        {
            int top = th;
            int bottom = h - 1;
            // Tail pointing up to Miko
            path.AddLine(tailX - tw / 2, top, tailX, 0);
            path.AddLine(tailX, 0, tailX + tw / 2, top);
            // Top edge right of tail
            path.AddLine(tailX + tw / 2, top, w - 1 - r, top);
            // Top-Right corner
            path.AddArc(w - 1 - d, top, d, d, 270, 90);
            // Right edge
            path.AddLine(w - 1, top + r, w - 1, bottom - r);
            // Bottom-Right corner
            path.AddArc(w - 1 - d, bottom - d, d, d, 0, 90);
            // Bottom edge
            path.AddLine(w - 1 - r, bottom, r, bottom);
            // Bottom-Left corner
            path.AddArc(0, bottom - d, d, d, 90, 90);
            // Left edge
            path.AddLine(0, bottom - r, 0, top + r);
            // Top-Left corner
            path.AddArc(0, top, d, d, 180, 90);
            // Top edge left of tail
            path.AddLine(r, top, tailX - tw / 2, top);
        }

        path.CloseFigure();
        return path;
    }
}
