using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Miko.Win32;

namespace Miko.UI;

public sealed class SpeechBubbleForm : Form
{
    private readonly Label _contentLabel;
    private readonly System.Windows.Forms.Timer _dismissTimer;
    private readonly System.Windows.Forms.Timer _typewriterTimer;

    private string _fullText = string.Empty;
    private int _typewriterIndex = 0;

    public SpeechBubbleForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        TopMost = true;
        BackColor = Color.FromArgb(12, 16, 23);
        Size = new Size(280, 110);

        _contentLabel = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = CyberpunkTheme.TextPrimary,
            Font = CyberpunkTheme.MonoFont,
            Padding = new Padding(14, 12, 14, 12),
            TextAlign = ContentAlignment.TopLeft,
            Cursor = Cursors.Hand
        };
        _contentLabel.Click += (s, e) => Hide();
        Controls.Add(_contentLabel);

        _dismissTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        _dismissTimer.Tick += (s, e) =>
        {
            _dismissTimer.Stop();
            Hide();
        };

        _typewriterTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _typewriterTimer.Tick += (s, e) =>
        {
            if (_typewriterIndex < _fullText.Length)
            {
                _typewriterIndex++;
                _contentLabel.Text = _fullText.Substring(0, _typewriterIndex) + "█";
            }
            else
            {
                _contentLabel.Text = _fullText;
                _typewriterTimer.Stop();
                _dismissTimer.Start();
            }
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

    public void ShowMessage(string text, Point companionAnchor, int companionWidth = 320)
    {
        _dismissTimer.Stop();
        _typewriterTimer.Stop();

        _fullText = text;
        _typewriterIndex = 0;
        _contentLabel.Text = "█";

        // Calculate size based on text length
        using (var g = CreateGraphics())
        {
            var size = g.MeasureString(text, CyberpunkTheme.MonoFont, 280);
            int wantedWidth = Math.Clamp((int)size.Width + 36, 220, 360);
            int wantedHeight = Math.Clamp((int)size.Height + 36, 70, 220);
            Size = new Size(wantedWidth, wantedHeight);
        }

        UpdatePosition(companionAnchor, companionWidth);

        if (!Visible)
        {
            Show();
        }

        _typewriterTimer.Start();
    }

    public void UpdatePosition(Point companionAnchor, int companionWidth = 320)
    {
        // Place bubble above companion if room exists, otherwise to the right or left
        var screen = Screen.FromPoint(companionAnchor);
        int bubbleX = companionAnchor.X + (companionWidth / 2) - (Width / 2);
        int bubbleY = companionAnchor.Y - Height - 12;

        if (bubbleY < screen.WorkingArea.Top + 10)
        {
            // Below or beside
            bubbleY = companionAnchor.Y + 180;
        }

        bubbleX = Math.Clamp(bubbleX, screen.WorkingArea.Left + 10, screen.WorkingArea.Right - Width - 10);
        Location = new Point(bubbleX, bubbleY);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var brush = new SolidBrush(CyberpunkTheme.CardBackground);
        using var borderPen = new Pen(CyberpunkTheme.Border, 1f);
        using var accentPen = new Pen(CyberpunkTheme.NeonCyan, 2f);

        // Draw card background
        g.FillRectangle(brush, rect);
        g.DrawRectangle(borderPen, rect);

        // Draw top neon accent bar
        g.DrawLine(accentPen, 0, 0, Width - 1, 0);

        // Corner accents
        g.DrawLine(accentPen, 0, 0, 0, 8);
        g.DrawLine(accentPen, Width - 1, 0, Width - 1, 8);
    }
}
