using System;
using System.Collections.Generic;
using System.IO;

namespace Miko.Animation;

public sealed class SpriteLibrary : IDisposable
{
    private readonly Dictionary<string, SpriteClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    public string AssetsDirectory { get; }

    public SpriteLibrary(string assetsDirectory)
    {
        AssetsDirectory = assetsDirectory;
        InitializeDefinitions();
    }

    private void InitializeDefinitions()
    {
        const double idleShiftX = 11.0 / 384.0 * 1.11;
        const double perchShiftX = -26.0 / 384.0;
        const double perchShiftY = 36.0 / 384.0;

        Register("idle", 60, 24, 1.11, idleShiftX, 0, true);
        Register("idle_blink", 8, 24, 1.11, idleShiftX, 0, false);
        Register("idle_blink_glitch", 8, 24, 1.11, idleShiftX, 0, false);

        Register("perch", 60, 30, 1.0, perchShiftX, perchShiftY, true);
        Register("perch_talk", 60, 30, 1.0, perchShiftX, perchShiftY, true);
        Register("perch_music", 60, 30, 1.0, perchShiftX, perchShiftY, true);
        Register("perch_blink", 8, 30, 1.0, perchShiftX, perchShiftY, false);
        Register("perch_blink_glitch", 8, 30, 1.0, perchShiftX, perchShiftY, false);
        Register("perch_glitch", 10, 15, 1.0, perchShiftX, perchShiftY, false);
        Register("perch_arrive", 14, 15, 1.0, perchShiftX, perchShiftY, false);
        Register("perch_leave", 12, 15, 1.0, perchShiftX, perchShiftY, false);

        Register("glitch", 10, 15, 1.11, 0, 0, false);
        Register("dangle", 24, 10, 1.11, 0, 0, true);
        Register("walk", 12, 12, 1.11, 0, 0, true);
        Register("dance", 16, 16, 1.11, 0, 0, true);
        Register("sleep", 16, 6, 1.11, 0, 0, true);
        Register("wake", 13, 15, 1.11, 0, 0, false);
    }

    private void Register(
        string name,
        int frameCount,
        double fps,
        double heightScale,
        double shiftX,
        double shiftY,
        bool isLooping)
    {
        string path = Path.Combine(AssetsDirectory, "sprites", $"{name}.png");
        _clips[name] = new SpriteClip(name, path, frameCount, fps, heightScale, shiftX, shiftY, isLooping);
    }

    public SpriteClip? GetClip(string name)
    {
        if (_clips.TryGetValue(name, out var clip))
        {
            clip.Load();
            return clip;
        }
        return null;
    }

    public void PreloadKeyClips()
    {
        GetClip("idle");
        GetClip("idle_blink");
        GetClip("perch");
        GetClip("perch_blink");
        GetClip("perch_glitch");
        GetClip("perch_talk");
        GetClip("glitch");
    }

    public void Dispose()
    {
        foreach (var clip in _clips.Values)
        {
            clip.Dispose();
        }
        _clips.Clear();
    }
}
