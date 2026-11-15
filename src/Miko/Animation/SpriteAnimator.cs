using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Miko.Animation;

public sealed class SpriteAnimator : IDisposable
{
    private readonly SpriteLibrary _library;
    private readonly Random _random = new();

    public int CanvasSize { get; }
    public double CompanionSize { get; set; } = 240.0;
    public bool IsFacingLeft { get; set; } = false;

    public AnimationState CurrentState { get; private set; } = AnimationState.Idle;
    public SpriteClip? CurrentClip { get; private set; }
    public int CurrentFrameIndex { get; private set; }

    // Pre-allocated surface to eliminate GC allocations
    public Bitmap SurfaceBitmap { get; }
    private readonly Graphics _surfaceGraphics;
    private readonly byte[] _alphaCache;

    // Timing and blinking
    private double _frameTimer = 0.0;
    private double _nextBlinkTime = 3.0;
    private bool _isSubClipActive = false;
    private SpriteClip? _fallbackClip = null;

    public event Action? FrameChanged;

    public SpriteAnimator(SpriteLibrary library, int canvasSize = 320, double companionSize = 240.0)
    {
        _library = library;
        CanvasSize = canvasSize;
        CompanionSize = companionSize;

        SurfaceBitmap = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppPArgb);
        _surfaceGraphics = Graphics.FromImage(SurfaceBitmap);
        _surfaceGraphics.CompositingMode = CompositingMode.SourceOver;
        _surfaceGraphics.CompositingQuality = CompositingQuality.HighQuality;
        _surfaceGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        _surfaceGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        _surfaceGraphics.SmoothingMode = SmoothingMode.HighQuality;

        _alphaCache = new byte[CanvasSize * CanvasSize];

        SetState(AnimationState.Idle, force: true);
    }

    public void SetState(AnimationState state, bool force = false)
    {
        if (CurrentState == state && !force) return;

        CurrentState = state;
        _isSubClipActive = false;
        _fallbackClip = null;
        CurrentFrameIndex = 0;
        _frameTimer = 0;

        switch (state)
        {
            case AnimationState.Idle:
                CurrentClip = _library.GetClip("idle");
                _nextBlinkTime = 2.0 + _random.NextDouble() * 4.0;
                break;
            case AnimationState.Perching:
                CurrentClip = _library.GetClip("perch");
                _nextBlinkTime = 2.0 + _random.NextDouble() * 4.0;
                break;
            case AnimationState.Thinking:
                CurrentClip = _library.GetClip("perch_music") ?? _library.GetClip("dance");
                break;
            case AnimationState.Talking:
                CurrentClip = CurrentState == AnimationState.Perching
                    ? (_library.GetClip("perch_talk") ?? _library.GetClip("perch"))
                    : (_library.GetClip("perch_talk") ?? _library.GetClip("idle"));
                break;
            case AnimationState.GlitchReact:
                CurrentClip = _library.GetClip("perch_glitch") ?? _library.GetClip("glitch");
                break;
            case AnimationState.Falling:
                CurrentClip = _library.GetClip("dangle") ?? _library.GetClip("glitch");
                break;
            case AnimationState.Walking:
                CurrentClip = _library.GetClip("walk");
                break;
            case AnimationState.Sleeping:
                CurrentClip = _library.GetClip("sleep");
                break;
        }

        RenderCurrentFrame();
        FrameChanged?.Invoke();
    }

    public void PlayOneShot(string clipName, Action? onCompleted = null)
    {
        var clip = _library.GetClip(clipName);
        if (clip == null) return;

        _fallbackClip = CurrentClip;
        CurrentClip = clip;
        CurrentFrameIndex = 0;
        _frameTimer = 0;
        _isSubClipActive = true;
        RenderCurrentFrame();
        FrameChanged?.Invoke();
    }

    public void Update(double deltaTimeSeconds)
    {
        if (CurrentClip == null || CurrentClip.Strip == null) return;

        _frameTimer += deltaTimeSeconds;
        double frameDuration = 1.0 / CurrentClip.Fps;

        // Check blink timer when resting
        if (!_isSubClipActive && (CurrentState == AnimationState.Idle || CurrentState == AnimationState.Perching))
        {
            _nextBlinkTime -= deltaTimeSeconds;
            if (_nextBlinkTime <= 0)
            {
                TriggerRandomBlink();
            }
        }

        if (_frameTimer >= frameDuration)
        {
            _frameTimer -= frameDuration;
            CurrentFrameIndex++;

            if (CurrentFrameIndex >= CurrentClip.FrameCount)
            {
                if (_isSubClipActive)
                {
                    // Return to primary clip
                    _isSubClipActive = false;
                    CurrentClip = _fallbackClip ?? (CurrentState == AnimationState.Perching ? _library.GetClip("perch") : _library.GetClip("idle"));
                    CurrentFrameIndex = 0;
                    _nextBlinkTime = 2.0 + _random.NextDouble() * 4.0;
                }
                else if (CurrentClip.IsLooping)
                {
                    CurrentFrameIndex = 0;
                }
                else
                {
                    CurrentFrameIndex = CurrentClip.FrameCount - 1;
                }
            }

            RenderCurrentFrame();
            FrameChanged?.Invoke();
        }
    }

    private void TriggerRandomBlink()
    {
        string blinkName = CurrentState == AnimationState.Perching
            ? (_random.NextDouble() > 0.3 ? "perch_blink" : "perch_blink_glitch")
            : (_random.NextDouble() > 0.3 ? "idle_blink" : "idle_blink_glitch");

        PlayOneShot(blinkName);
        _nextBlinkTime = 2.5 + _random.NextDouble() * 4.0;
    }

    public void RenderCurrentFrame()
    {
        if (CurrentClip?.Strip == null) return;

        _surfaceGraphics.Clear(Color.Transparent);

        int cellWidth = CurrentClip.CellWidth;
        int cellHeight = CurrentClip.CellHeight;
        int frameIndex = Math.Clamp(CurrentFrameIndex, 0, CurrentClip.FrameCount - 1);

        float tall = (float)(CompanionSize * CurrentClip.HeightScale);
        float wide = tall * cellWidth / cellHeight;
        float shiftX = (float)(CurrentClip.ShiftX * CompanionSize);
        float shiftY = (float)(CurrentClip.ShiftY * CompanionSize);

        var state = _surfaceGraphics.Save();
        try
        {
            float centerX = CanvasSize / 2f;
            float centerY = CanvasSize / 2f;

            _surfaceGraphics.TranslateTransform(centerX, centerY);

            if (IsFacingLeft)
            {
                _surfaceGraphics.ScaleTransform(-1, 1);
            }

            var targetRect = new RectangleF(-wide / 2f + shiftX, -tall / 2f + shiftY, wide, tall);
            var srcRect = new Rectangle(frameIndex * cellWidth, 0, cellWidth, cellHeight);

            _surfaceGraphics.DrawImage(CurrentClip.Strip, targetRect, srcRect, GraphicsUnit.Pixel);
        }
        finally
        {
            _surfaceGraphics.Restore(state);
        }

        UpdateAlphaCache();
    }

    private unsafe void UpdateAlphaCache()
    {
        var data = SurfaceBitmap.LockBits(
            new Rectangle(0, 0, CanvasSize, CanvasSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);

        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;

            for (int y = 0; y < CanvasSize; y++)
            {
                byte* row = scan0 + (y * stride);
                int cacheOffset = y * CanvasSize;
                for (int x = 0; x < CanvasSize; x++)
                {
                    // In 32bpp PARGB, alpha is at offset 3
                    _alphaCache[cacheOffset + x] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            SurfaceBitmap.UnlockBits(data);
        }
    }

    public bool IsSolidPixel(int x, int y, byte threshold = 12)
    {
        // 3x3 kernel test for generous hit box on character outline
        for (int dy = -2; dy <= 2; dy += 2)
        {
            for (int dx = -2; dx <= 2; dx += 2)
            {
                int sampleX = x + dx;
                int sampleY = y + dy;
                if (sampleX >= 0 && sampleX < CanvasSize && sampleY >= 0 && sampleY < CanvasSize)
                {
                    if (_alphaCache[sampleY * CanvasSize + sampleX] >= threshold)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public void Dispose()
    {
        _surfaceGraphics.Dispose();
        SurfaceBitmap.Dispose();
    }
}
