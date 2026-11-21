using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Miko.Security;

public static class SecurityGuard
{
    private static readonly HashSet<string> AllowedApiHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "generativelanguage.googleapis.com",
        "api.fish.audio"
    };

    // Hard-coded default allowlist of safe applications
    private static readonly Dictionary<string, (string Exe, string Name)> DefaultAllowlist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "notepad", ("notepad.exe", "Notepad") },
            { "calc", ("calc.exe", "Calculator") },
            { "calculator", ("calc.exe", "Calculator") },
            { "chrome", ("chrome.exe", "Google Chrome") },
            { "spotify", ("spotify.exe", "Spotify") },
            { "discord", ("discord.exe", "Discord") },
            { "vscode", ("code.cmd", "Visual Studio Code") },
            { "code", ("code.cmd", "Visual Studio Code") },
            { "mspaint", ("mspaint.exe", "Paint") },
            { "paint", ("mspaint.exe", "Paint") }
        };

    // Sensitive folder prefixes that Miko must never access
    private static readonly Lazy<List<string>> SensitiveFolders = new(() =>
    {
        var list = new List<string>();
        void AddFolder(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                list.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.System));
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        AddFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            list.Add(Path.Combine(userProfile, ".ssh"));
            list.Add(Path.Combine(userProfile, ".aws"));
            list.Add(Path.Combine(userProfile, ".azure"));
            list.Add(Path.Combine(userProfile, ".kube"));
        }
        return list;
    });

    private static readonly Regex ApiKeyRegex = new(@"AIza[0-9A-Za-z-_]{35}", RegexOptions.Compiled);
    private static readonly Regex FishApiKeyRegex = new(@"sk-fish-[0-9A-Za-z-_]+", RegexOptions.Compiled);
    private static readonly Regex QueryKeyRegex = new(@"(key=)[A-Za-z0-9_\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BearerKeyRegex = new(@"(Bearer\s+)[A-Za-z0-9_\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Validates that outbound network requests are strictly limited to official Google Gemini and Fish Audio hosts.
    /// </summary>
    public static void ValidateOutboundEndpoint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Invalid API endpoint URI format.");
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Insecure HTTP scheme rejected. Outbound connections must use TLS (HTTPS).");
        }

        if (!AllowedApiHosts.Contains(uri.Host))
        {
            throw new InvalidOperationException($"Unauthorized outbound host '{uri.Host}'. Only official Gemini and Fish Audio hosts are permitted.");
        }
    }

    /// <summary>
    /// Strictly checks an application name against the hardcoded and configured allowlist.
    /// Prevents directory traversal, command injection, and arbitrary URI/file launching.
    /// </summary>
    public static bool TryResolveAllowlistedApp(
        string rawInput,
        Dictionary<string, string>? customAllowlist,
        out string executable,
        out string displayName)
    {
        executable = string.Empty;
        displayName = string.Empty;

        if (string.IsNullOrWhiteSpace(rawInput)) return false;

        string cleaned = rawInput.Trim().ToLowerInvariant();

        // Reject directory traversal, arguments, script injection, and URI protocols
        if (cleaned.Contains("..") ||
            cleaned.Contains('/') ||
            cleaned.Contains('\\') ||
            cleaned.Contains(':') ||
            cleaned.Contains(';') ||
            cleaned.Contains('&') ||
            cleaned.Contains('|') ||
            cleaned.Contains('`') ||
            cleaned.Contains('$') ||
            cleaned.Contains('%') ||
            cleaned.Contains('\"') ||
            cleaned.Contains('\''))
        {
            return false;
        }

        // Strip known executable extensions if provided
        if (cleaned.EndsWith(".exe") || cleaned.EndsWith(".cmd"))
        {
            cleaned = Path.GetFileNameWithoutExtension(cleaned);
        }

        // 1. Check custom configured allowlist
        if (customAllowlist != null && customAllowlist.TryGetValue(cleaned, out var customExe))
        {
            // Verify custom exe does not contain path traversal
            if (!customExe.Contains("..") && !customExe.Contains('/') && !customExe.Contains('\\'))
            {
                executable = customExe;
                displayName = cleaned;
                return true;
            }
        }

        // 2. Check hardcoded default allowlist
        if (DefaultAllowlist.TryGetValue(cleaned, out var match))
        {
            executable = match.Exe;
            displayName = match.Name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a given path falls within sensitive system or credential directories.
    /// </summary>
    public static bool IsSensitivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;

        try
        {
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fileName = Path.GetFileName(fullPath).ToLowerInvariant();

            // Block sensitive file names and extensions
            if (fileName.StartsWith(".env") ||
                fileName.EndsWith(".key") ||
                fileName.EndsWith(".pem") ||
                fileName == "id_rsa" ||
                fileName == "id_ed25519" ||
                fileName.Contains("credential") ||
                fileName.Contains("history") ||
                fileName == "appsettings.json")
            {
                return true;
            }

            // Block system folders
            foreach (var sensitiveDir in SensitiveFolders.Value)
            {
                if (fullPath.Equals(sensitiveDir, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(sensitiveDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // Fail closed on any resolution error
            return true;
        }
    }

    /// <summary>
    /// Displays a native Windows Human-in-the-Loop confirmation dialog.
    /// Default choice is 'No' (deny) to enforce least privilege.
    /// </summary>
    public static bool ConfirmActionWithUser(string actionName, string actionDetails)
    {
        string message =
            $"[MIKO SECURITY GUARDRAIL]\n\n" +
            $"Miko is requesting authorization to perform the following action on your system:\n\n" +
            $"• Action:  {actionName}\n" +
            $"• Details: {actionDetails}\n\n" +
            $"Do you explicitly authorize this action?";

        var result = MessageBox.Show(
            message,
            "Miko — Security Authorization",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2); // Default to 'No'

        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Redacts API keys and sensitive tokens from strings before they are logged or shown on screen.
    /// </summary>
    public static string Sanitize(string? text, params string?[] sensitiveKeys)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string sanitized = text;

        if (sensitiveKeys != null)
        {
            foreach (var key in sensitiveKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    sanitized = sanitized.Replace(key, "[REDACTED_API_KEY]");
                }
            }
        }

        sanitized = ApiKeyRegex.Replace(sanitized, "[REDACTED_API_KEY]");
        sanitized = FishApiKeyRegex.Replace(sanitized, "[REDACTED_API_KEY]");
        sanitized = QueryKeyRegex.Replace(sanitized, "$1[REDACTED_API_KEY]");
        sanitized = BearerKeyRegex.Replace(sanitized, "$1[REDACTED_API_KEY]");

        return sanitized;
    }
}
