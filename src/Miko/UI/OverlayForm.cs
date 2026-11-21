using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Miko.Animation;
using Miko.Physics;
using Miko.Voice;
using Miko.Win32;

namespace Miko.UI;

public sealed class OverlayForm : Form
{
    private readonly SpriteAnimator _animator;
    private readonly PhysicsEngine _physics;
    private readonly VoiceEngine? _voiceEngine;
    private readonly System.Windows.Forms.Timer _tickTimer;

    private IntPtr _memoryDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private bool _isDisposed = false;
    private int _idleTrimCounter = 0;

    public event Action? RequestChatPrompt;
    public event Action? RequestExit;

    public OverlayForm(SpriteAnimator animator, PhysicsEngine physics, VoiceEngine? voiceEngine = null)
    {
        _animator = animator;
        _physics = physics;
        _voiceEngine = voiceEngine;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(_animator.CanvasSize, _animator.CanvasSize);
        Location = new Point(_physics.AnchorX, _physics.AnchorY);
        TopMost = true;

        // Initialize Layered Window Device Contexts
        InitializeLayeredDC();

        _animator.FrameChanged += OnFrameChanged;
        _physics.PositionChanged += OnPositionChanged;

        // Create Context Menu
        CreateContextMenu();

        // High efficiency 30 Hz timer
        _tickTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        _tickTimer.Tick += OnTick;
        _tickTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED |
                          NativeMethods.WS_EX_TOOLWINDOW |
                          NativeMethods.WS_EX_TOPMOST |
                          NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    private void InitializeLayeredDC()
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            _memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            _hBitmap = _animator.SurfaceBitmap.GetHbitmap(Color.FromArgb(0));
            _oldBitmap = NativeMethods.SelectObject(_memoryDc, _hBitmap);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var talkItem = new ToolStripMenuItem("Talk to Miko... (Ctrl+Alt+T)");
        talkItem.Click += (s, e) => RequestChatPrompt?.Invoke();
        menu.Items.Add(talkItem);

        var muteVoiceItem = new ToolStripMenuItem("Mute Voice");
        muteVoiceItem.Click += (s, e) =>
        {
            if (_voiceEngine != null)
            {
                _voiceEngine.ToggleMute();
                muteVoiceItem.Text = _voiceEngine.IsMuted ? "Unmute Voice" : "Mute Voice";
            }
        };
        menu.Opening += (s, e) =>
        {
            if (_voiceEngine != null)
            {
                muteVoiceItem.Text = _voiceEngine.IsMuted ? "Unmute Voice" : "Mute Voice";
            }
        };
        menu.Items.Add(muteVoiceItem);

        menu.Items.Add(new ToolStripSeparator());

        var statesMenu = new ToolStripMenuItem("Animation State");
        statesMenu.DropDownItems.Add("Idle (Standing)", null, (s, e) =>
        {
            _physics.Land(new Ledge { IsFloor = true, Y = Screen.PrimaryScreen!.WorkingArea.Bottom });
        });
        statesMenu.DropDownItems.Add("Perching (Window)", null, (s, e) =>
        {
            _animator.SetState(AnimationState.Perching);
        });
        statesMenu.DropDownItems.Add("Thinking", null, (s, e) => _animator.SetState(AnimationState.Thinking));
        statesMenu.DropDownItems.Add("Talking", null, (s, e) => _animator.SetState(AnimationState.Talking));
        statesMenu.DropDownItems.Add("Glitch / React", null, (s, e) => _animator.SetState(AnimationState.GlitchReact));
        statesMenu.DropDownItems.Add("Sleep", null, (s, e) => _animator.SetState(AnimationState.Sleeping));
        menu.Items.Add(statesMenu);

        var dropItem = new ToolStripMenuItem("Drop (Trigger Gravity)");
        dropItem.Click += (s, e) => _physics.StartFalling();
        menu.Items.Add(dropItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Miko");
        exitItem.Click += (s, e) => RequestExit?.Invoke();
        menu.Items.Add(exitItem);

        ContextMenuStrip = menu;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double dt = 0.033;
        _physics.Tick(dt);
        _animator.Update(dt);

        // Throttle timer when idle and stationary
        bool isStationary = !_physics.IsFalling && !_physics.IsDragging &&
                            (_animator.CurrentState == AnimationState.Idle || _animator.CurrentState == AnimationState.Perching);

        if (isStationary)
        {
            _idleTrimCounter++;
            if (_idleTrimCounter >= 600) // ~25 seconds of stationary idle
            {
                _idleTrimCounter = 0;
                Program.TrimWorkingSet();
            }
        }
        else
        {
            _idleTrimCounter = 0;
        }

        int targetInterval = isStationary ? 42 : 33;
        if (_tickTimer.Interval != targetInterval)
        {
            _tickTimer.Interval = targetInterval;
        }
    }

    private void OnPositionChanged(int x, int y)
    {
        if (_isDisposed || !IsHandleCreated) return;
        Location = new Point(x, y);
        RedrawLayeredWindow();
    }

    private void OnFrameChanged()
    {
        if (_isDisposed || !IsHandleCreated) return;
        RedrawLayeredWindow();
    }

    public void RedrawLayeredWindow()
    {
        if (_isDisposed || !IsHandleCreated) return;

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            // Replace HBitmap in memory DC with updated surface bitmap
            IntPtr newHBitmap = _animator.SurfaceBitmap.GetHbitmap(Color.FromArgb(0));
            NativeMethods.SelectObject(_memoryDc, newHBitmap);
            if (_hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_hBitmap);
            }
            _hBitmap = newHBitmap;

            POINT dstPoint = new POINT(Location.X, Location.Y);
            SIZE size = new SIZE(Width, Height);
            POINT srcPoint = new POINT(0, 0);
            BLENDFUNCTION blend = new BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            NativeMethods.UpdateLayeredWindow(
                Handle,
                screenDc,
                ref dstPoint,
                ref size,
                _memoryDc,
                ref srcPoint,
                0,
                ref blend,
                NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_NCHITTEST:
                // Test pixel under cursor: if transparent, pass through to underlying window!
                long lParam = m.LParam.ToInt64();
                int screenX = (short)(lParam & 0xFFFF);
                int screenY = (short)((lParam >> 16) & 0xFFFF);
                Point clientPt = PointToClient(new Point(screenX, screenY));

                if (!_animator.IsSolidPixel(clientPt.X, clientPt.Y))
                {
                    m.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
                    return;
                }
                m.Result = new IntPtr(NativeMethods.HTCLIENT);
                return;

            case NativeMethods.WM_LBUTTONDOWN:
                _physics.StartDrag(Cursor.Position);
                Capture = true;
                return;

            case NativeMethods.WM_MOUSEMOVE:
                if (_physics.IsDragging)
                {
                    _physics.UpdateDrag(Cursor.Position);
                }
                return;

            case NativeMethods.WM_LBUTTONUP:
                if (_physics.IsDragging)
                {
                    Capture = false;
                    _physics.EndDrag();
                }
                return;

            case NativeMethods.WM_RBUTTONUP:
                ContextMenuStrip?.Show(Cursor.Position);
                return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _tickTimer.Stop();
            _tickTimer.Dispose();

            if (_memoryDc != IntPtr.Zero)
            {
                if (_oldBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(_memoryDc, _oldBitmap);
                }
                NativeMethods.DeleteDC(_memoryDc);
                _memoryDc = IntPtr.Zero;
            }

            if (_hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_hBitmap);
                _hBitmap = IntPtr.Zero;
            }
        }

        base.Dispose(disposing);
    }
}
