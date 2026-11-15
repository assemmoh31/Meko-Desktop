using System;
using System.Collections.Generic;
using System.Drawing;
using Miko.Animation;
using Miko.Win32;

namespace Miko.Physics;

public sealed class PhysicsEngine
{
    private const double SitX = 0.30;
    private const double SitY = 0.79;
    private const double StandX = 0.35;
    private const double StandY = 0.95;
    private const double Gravity = 1800.0;
    private const double MaxFallSpeed = 1400.0;

    private readonly SpriteAnimator _animator;
    private readonly HashSet<IntPtr> _ignoredHwnds = new();

    public int AnchorX { get; set; } = 300;
    public int AnchorY { get; set; } = 300;
    public double CompanionSize => _animator.CompanionSize;

    public Ledge? CurrentSeat { get; private set; }
    public bool IsFalling { get; private set; } = false;
    public bool IsDragging { get; private set; } = false;

    private double _fallSpeed = 0.0;
    private Point _dragOffset;

    public event Action<int, int>? PositionChanged;

    public PhysicsEngine(SpriteAnimator animator)
    {
        _animator = animator;
    }

    public void RegisterIgnoredWindow(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            _ignoredHwnds.Add(hWnd);
        }
    }

    public Point GetHipPoint()
    {
        bool isStanding = CurrentSeat?.IsFloor == true;
        double factorX = isStanding ? StandX : SitX;
        double factorY = isStanding ? StandY : SitY;

        return new Point(
            AnchorX + (int)(CompanionSize * factorX),
            AnchorY + (int)(CompanionSize * factorY));
    }

    public Point SeatedAnchor(int hipX, int ledgeY)
    {
        return new Point(
            hipX - (int)(CompanionSize * SitX),
            ledgeY - (int)(CompanionSize * SitY));
    }

    public Point StandingAnchor(int hipX, int floorY)
    {
        return new Point(
            hipX - (int)(CompanionSize * StandX),
            floorY - (int)(CompanionSize * StandY));
    }

    public void StartDrag(Point cursorScreenPos)
    {
        IsDragging = true;
        IsFalling = false;
        CurrentSeat = null;
        _fallSpeed = 0;
        _dragOffset = new Point(cursorScreenPos.X - AnchorX, cursorScreenPos.Y - AnchorY);
        _animator.SetState(AnimationState.GlitchReact);
    }

    public void UpdateDrag(Point cursorScreenPos)
    {
        if (!IsDragging) return;

        int newX = cursorScreenPos.X - _dragOffset.X;
        int newY = cursorScreenPos.Y - _dragOffset.Y;

        // Facing direction based on drag delta
        if (newX > AnchorX + 2) _animator.IsFacingLeft = false;
        else if (newX < AnchorX - 2) _animator.IsFacingLeft = true;

        AnchorX = newX;
        AnchorY = newY;
        PositionChanged?.Invoke(AnchorX, AnchorY);
    }

    public void EndDrag()
    {
        if (!IsDragging) return;
        IsDragging = false;

        // Check if cursor dropped right on or near an open window title bar (snap perch)
        var hip = GetHipPoint();
        var ledges = LedgeScanner.ScanActiveLedges(_ignoredHwnds);
        var nearLedge = LedgeScanner.FindBestLedgeUnder(ledges, hip.X, hip.Y, 40);

        if (nearLedge != null && Math.Abs(nearLedge.Y - hip.Y) <= 30)
        {
            Land(nearLedge);
        }
        else
        {
            StartFalling();
        }
    }

    public void StartFalling()
    {
        IsFalling = true;
        CurrentSeat = null;
        _fallSpeed = 80.0; // Initial push
        _animator.SetState(AnimationState.Falling);
    }

    public void Land(Ledge ledge)
    {
        IsFalling = false;
        _fallSpeed = 0;
        CurrentSeat = ledge;

        var hip = GetHipPoint();
        int targetX = Math.Clamp(hip.X, ledge.Left + 16, Math.Max(ledge.Left + 16, ledge.Right - 16));

        Point newAnchor = ledge.IsFloor
            ? StandingAnchor(targetX, ledge.Y)
            : SeatedAnchor(targetX, ledge.Y);

        AnchorX = newAnchor.X;
        AnchorY = newAnchor.Y;
        PositionChanged?.Invoke(AnchorX, AnchorY);

        if (ledge.IsFloor)
        {
            _animator.SetState(AnimationState.Idle);
        }
        else
        {
            _animator.SetState(AnimationState.Perching);
            _animator.PlayOneShot("perch_arrive");
        }
    }

    public void Tick(double deltaTimeSeconds)
    {
        if (IsDragging) return;

        if (IsFalling)
        {
            UpdateFalling(deltaTimeSeconds);
            return;
        }

        if (CurrentSeat != null)
        {
            MaintainSeat();
        }
    }

    private void UpdateFalling(double deltaTimeSeconds)
    {
        _fallSpeed = Math.Min(MaxFallSpeed, _fallSpeed + (Gravity * deltaTimeSeconds));
        int dropDistance = (int)Math.Ceiling(_fallSpeed * deltaTimeSeconds);

        var hip = GetHipPoint();
        var ledges = LedgeScanner.ScanActiveLedges(_ignoredHwnds);
        var targetLedge = LedgeScanner.FindBestLedgeUnder(ledges, hip.X, hip.Y, dropDistance + 20);

        if (targetLedge != null && hip.Y + dropDistance >= targetLedge.Y)
        {
            Land(targetLedge);
        }
        else
        {
            AnchorY += dropDistance;
            PositionChanged?.Invoke(AnchorX, AnchorY);
        }
    }

    private void MaintainSeat()
    {
        if (CurrentSeat == null) return;

        // If sitting on desktop floor
        if (CurrentSeat.IsFloor) return;

        // If sitting on a window, check if window still exists, is visible and not minimized
        if (!LedgeScanner.TryGetWindowBounds(CurrentSeat.Window, out var newBounds))
        {
            // Window was closed or minimized! Detach and fall!
            StartFalling();
            return;
        }

        // Check if window moved
        if (newBounds.Left != CurrentSeat.Bounds.Left ||
            newBounds.Top != CurrentSeat.Bounds.Top ||
            newBounds.Right != CurrentSeat.Bounds.Right ||
            newBounds.Bottom != CurrentSeat.Bounds.Bottom)
        {
            int deltaX = newBounds.Left - CurrentSeat.Bounds.Left;
            CurrentSeat.Bounds = newBounds;
            CurrentSeat.Left = newBounds.Left;
            CurrentSeat.Right = newBounds.Right;
            CurrentSeat.Y = newBounds.Top;

            var hip = GetHipPoint();
            int clampedX = Math.Clamp(hip.X + deltaX, CurrentSeat.Left + 16, Math.Max(CurrentSeat.Left + 16, CurrentSeat.Right - 16));
            var newAnchor = SeatedAnchor(clampedX, CurrentSeat.Y);

            AnchorX = newAnchor.X;
            AnchorY = newAnchor.Y;
            PositionChanged?.Invoke(AnchorX, AnchorY);
        }
    }
}
