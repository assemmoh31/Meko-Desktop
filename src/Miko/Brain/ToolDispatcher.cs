using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Miko.Animation;
using Miko.Security;
using Miko.Win32;

namespace Miko.Brain;

public sealed class ToolDispatcher
{
    private readonly SpriteAnimator _animator;
    private readonly AppConfig _config;

    // Virtual key codes for media control
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP = 0xB2;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public ToolDispatcher(SpriteAnimator animator, AppConfig config)
    {
        _animator = animator;
        _config = config;
    }

    public string Execute(string toolName, string argumentsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            return toolName switch
            {
                "open_app" => OpenAllowlistedApp(root.GetProperty("app_name").GetString() ?? ""),
                "control_media" => ControlMedia(root.GetProperty("action").GetString() ?? ""),
                "get_active_window" => GetActiveWindow(),
                "set_character_state" => SetCharacterState(root.GetProperty("state").GetString() ?? ""),
                "run_command" => "Security Guardrail: Arbitrary shell command execution is permanently disabled.",
                _ => $"Security error: Unknown or unauthorized tool '{toolName}'."
            };
        }
        catch (Exception ex)
        {
            return $"Tool error: {ex.Message}";
        }
    }

    private string OpenAllowlistedApp(string rawAppName)
    {
        // 1. Strict Allowlist & Path Traversal Validation
        if (!SecurityGuard.TryResolveAllowlistedApp(rawAppName, _config.AllowedApps, out var executable, out var displayName))
        {
            return $"Security Guardrail: '{rawAppName}' is not in the authorized application allowlist. Request blocked.";
        }

        // 2. Mandatory Human-in-the-Loop Confirmation
        if (_config.RequireConfirmation)
        {
            bool authorized = SecurityGuard.ConfirmActionWithUser(
                actionName: "Launch Application",
                actionDetails: $"{displayName} ({executable})");

            if (!authorized)
            {
                return $"Action blocked: Operator declined authorization to open {displayName}.";
            }
        }

        // 3. Execution under Least Privilege
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true
            };
            Process.Start(psi);
            return $"Successfully launched {displayName}.";
        }
        catch (Exception ex)
        {
            return $"Failed to open {displayName}: {ex.Message}";
        }
    }

    private string ControlMedia(string action)
    {
        action = action.ToLowerInvariant().Trim();
        byte vk = action switch
        {
            "play_pause" or "play" or "pause" => VK_MEDIA_PLAY_PAUSE,
            "next" => VK_MEDIA_NEXT_TRACK,
            "prev" or "previous" => VK_MEDIA_PREV_TRACK,
            "volume_up" => VK_VOLUME_UP,
            "volume_down" => VK_VOLUME_DOWN,
            "mute" => VK_VOLUME_MUTE,
            _ => 0
        };

        if (vk == 0) return $"Invalid media action '{action}'. Valid: play_pause, next, prev, volume_up, volume_down, mute.";

        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
        return $"Sent media signal: {action}.";
    }

    private string GetActiveWindow()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return "No active window is currently focused.";

        var sbTitle = new StringBuilder(256);
        NativeMethods.GetWindowText(fg, sbTitle, sbTitle.Capacity);
        string title = sbTitle.ToString().Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return "The active window has no title.";
        }

        return $"The user is currently focused on the window: \"{title}\"";
    }

    private string SetCharacterState(string state)
    {
        if (Enum.TryParse<AnimationState>(state, true, out var animState))
        {
            _animator.SetState(animState);
            return $"Miko transitioned to {animState}.";
        }
        return $"Invalid animation state '{state}'. Valid: Idle, Perching, Thinking, Talking, GlitchReact, Sleeping";
    }
}
