using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Miko.Security;

namespace Miko;

public sealed class AppConfig
{
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-3.5-flash-lite";
    public string ApiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public double CompanionSize { get; set; } = 240.0;
    public string Personality { get; set; } = string.Empty;
    public string UserContext { get; set; } = string.Empty;
    public Dictionary<string, string> AllowedApps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool RequireConfirmation { get; set; } = true;
    public bool VoiceEnabled { get; set; } = true;
    public string VoiceProvider { get; set; } = "fishaudio";
    public string FishApiKey { get; set; } = string.Empty;
    public string FishModel { get; set; } = "s2.1-pro-free";
    public string FishVoiceId { get; set; } = "fdef76c9b2e741119d817754bd8b5dd0";
    public string FallbackVoiceProvider { get; set; } = "windows";

    public static AppConfig Load(string rootDir)
    {
        var config = new AppConfig();
        string configPath = Path.Combine(rootDir, "config", "appsettings.json");

        if (File.Exists(configPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("Gemini", out var gemini))
                {
                    if (gemini.TryGetProperty("ApiKey", out var key)) config.GeminiApiKey = key.GetString() ?? "";
                    if (gemini.TryGetProperty("Model", out var model)) config.GeminiModel = model.GetString() ?? "gemini-3.5-flash-lite";
                    if (gemini.TryGetProperty("ApiEndpoint", out var ep)) config.ApiEndpoint = ep.GetString() ?? config.ApiEndpoint;
                }
                if (root.TryGetProperty("Companion", out var comp))
                {
                    if (comp.TryGetProperty("Size", out var size)) config.CompanionSize = size.GetDouble();
                }
                if (root.TryGetProperty("Voice", out var voice))
                {
                    if (voice.TryGetProperty("Enabled", out var ve)) config.VoiceEnabled = ve.GetBoolean();
                    if (voice.TryGetProperty("Provider", out var vp)) config.VoiceProvider = vp.GetString() ?? "fishaudio";
                    if (voice.TryGetProperty("FishApiKey", out var fk)) config.FishApiKey = fk.GetString() ?? "";
                    if (voice.TryGetProperty("FishModel", out var fm)) config.FishModel = fm.GetString() ?? "s2.1-pro-free";
                    if (voice.TryGetProperty("FishVoiceId", out var fv)) config.FishVoiceId = fv.GetString() ?? "fdef76c9b2e741119d817754bd8b5dd0";
                    if (voice.TryGetProperty("FallbackProvider", out var fb)) config.FallbackVoiceProvider = fb.GetString() ?? "windows";
                }
                if (root.TryGetProperty("Security", out var sec))
                {
                    if (sec.TryGetProperty("RequireConfirmation", out var rc)) config.RequireConfirmation = rc.GetBoolean();
                    if (sec.TryGetProperty("AllowedApps", out var apps) && apps.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in apps.EnumerateObject())
                        {
                            config.AllowedApps[prop.Name] = prop.Value.GetString() ?? "";
                        }
                    }
                }
            }
            catch { }
        }

        // Environment variable override
        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            config.GeminiApiKey = envKey.Trim();
        }

        string? envFishKey = Environment.GetEnvironmentVariable("FISH_API_KEY");
        if (!string.IsNullOrWhiteSpace(envFishKey))
        {
            config.FishApiKey = envFishKey.Trim();
        }

        // Validate outbound endpoint strictly
        try
        {
            SecurityGuard.ValidateOutboundEndpoint(config.ApiEndpoint);
        }
        catch
        {
            // Reset to default secure endpoint if misconfigured
            config.ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta";
        }

        string personalityPath = Path.Combine(rootDir, "config", "personality.md");
        if (File.Exists(personalityPath))
        {
            config.Personality = File.ReadAllText(personalityPath);
        }

        string mePath = Path.Combine(rootDir, "config", "me.md");
        if (File.Exists(mePath))
        {
            config.UserContext = File.ReadAllText(mePath);
        }

        return config;
    }
}
