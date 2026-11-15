using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Miko.Win32;

namespace Miko.UI;

public sealed class ChatPromptForm : Form
{
    private readonly TextBox _inputBox;
    private readonly Label _titleLabel;

    public event Action<string>? MessageSubmitted;

    public ChatPromptForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        TopMost = true;
        BackColor = Color.FromArgb(10, 14, 20);
        Size = new Size(460, 80);

        _titleLabel = new Label
        {
            Text = "MIKO // DIRECT NEURAL LINK [ENTER TO TRANSMIT | ESC TO ABORT]",
            Font = new Font(CyberpunkTheme.MonoFont.FontFamily, 8.5f, FontStyle.Bold),
            ForeColor = CyberpunkTheme.NeonCyan,
            BackColor = Color.Transparent,
            Location = new Point(16, 12),
            Size = new Size(428, 18)
        };
        Controls.Add(_titleLabel);

        _inputBox = new TextBox
        {
            Font = CyberpunkTheme.MonoFont,
            ForeColor = CyberpunkTheme.TextPrimary,
            BackColor = Color.FromArgb(18, 24, 34),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(16, 36),
            Size = new Size(428, 28)
        };
        _inputBox.KeyDown += OnInputKeyDown;
        Controls.Add(_inputBox);

        Deactivate += (s, e) => Hide();
    }

    public void OpenPrompt(Point? preferredLocation = null)
    {
        _inputBox.Text = string.Empty;
        if (preferredLocation.HasValue)
        {
            var screen = Screen.FromPoint(preferredLocation.Value);
            int x = Math.Clamp(preferredLocation.Value.X - (Width / 2), screen.WorkingArea.Left + 20, screen.WorkingArea.Right - Width - 20);
            int y = Math.Clamp(preferredLocation.Value.Y - Height - 20, screen.WorkingArea.Top + 20, screen.WorkingArea.Bottom - Height - 20);
            Location = new Point(x, y);
        }
        else
        {
            CenterToScreen();
        }

        Show();
        NativeMethods.SetForegroundWindow(Handle);
        _inputBox.Focus();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            string text = _inputBox.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                Hide();
                MessageSubmitted?.Invoke(text);
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var penBorder = new Pen(CyberpunkTheme.Border, 1f);
        using var penAccent = new Pen(CyberpunkTheme.NeonCyan, 2f);

        g.DrawRectangle(penBorder, rect);
        g.DrawLine(penAccent, 0, 0, Width - 1, 0);
        g.DrawLine(penAccent, 0, 0, 0, 10);
        g.DrawLine(penAccent, Width - 1, 0, Width - 1, 10);
    }
}
