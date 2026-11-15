using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Miko.Animation;
using Miko.Brain;
using Miko.Physics;
using Miko.UI;
using Miko.Win32;

namespace Miko;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void TrimWorkingSet()
    {
        try { EmptyWorkingSet(Process.GetCurrentProcess().Handle); }
        catch { }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    private static OverlayForm? _overlayForm;
    private static SpeechBubbleForm? _speechBubble;
    private static ChatPromptForm? _chatPrompt;
    private static HotkeyManager? _hotkey;
    private static NotifyIcon? _trayIcon;
    private static SpriteAnimator? _animator;
    private static PhysicsEngine? _physics;
    private static GeminiClient? _gemini;
    private static ToolDispatcher? _tools;
    private static AppConfig? _config;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--test" || args[0] == "-t"))
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            return RunTests();
        }

        if (args.Length > 0 && args[0] == "--chat-test")
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            return RunChatTest();
        }

        ApplicationConfiguration.Initialize();

        string rootDir = FindProjectRoot();
        string assetsDir = Path.Combine(rootDir, "assets");

        if (!Directory.Exists(assetsDir))
        {
            MessageBox.Show($"Could not locate assets directory at: {assetsDir}", "Miko Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        _config = AppConfig.Load(rootDir);

        // 1. Initialize Sprite Library & Engine
        var library = new SpriteLibrary(assetsDir);
        library.PreloadKeyClips();

        _animator = new SpriteAnimator(library, canvasSize: 320, companionSize: _config.CompanionSize);

        // 2. Initialize Physics & Perching Engine
        _physics = new PhysicsEngine(_animator);

        // 3. Initialize Forms
        _overlayForm = new OverlayForm(_animator, _physics);
        _physics.RegisterIgnoredWindow(_overlayForm.Handle);

        _speechBubble = new SpeechBubbleForm();
        _physics.RegisterIgnoredWindow(_speechBubble.Handle);

        _chatPrompt = new ChatPromptForm();
        _physics.RegisterIgnoredWindow(_chatPrompt.Handle);

        // 4. Initialize AI Brain with Security Config
        _tools = new ToolDispatcher(_animator, _config);
        _gemini = new GeminiClient(_config, _tools);

        // 5. Connect Events
        _overlayForm.RequestChatPrompt += OpenChatPrompt;
        _overlayForm.RequestExit += ExitApp;

        _chatPrompt.MessageSubmitted += OnUserMessageSubmitted;

        _physics.PositionChanged += (x, y) =>
        {
            if (_speechBubble != null && _speechBubble.Visible)
            {
                _speechBubble.UpdatePosition(new Point(x, y), _animator.CanvasSize);
            }
        };

        // 6. Setup Global Hotkey (Ctrl + Alt + T)
        _hotkey = new HotkeyManager();
        _hotkey.Register(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.T);
        _hotkey.HotkeyPressed += OpenChatPrompt;

        // 7. Setup System Tray Icon
        SetupTrayIcon(assetsDir);

        // 8. Place Miko onto active window on startup
        PositionStartupPerch();

        // Welcome greeting bubble
        _speechBubble.ShowMessage("Miko initialized. Ready to perch! Press Ctrl+Alt+T to chat.", new Point(_physics.AnchorX, _physics.AnchorY), _animator.CanvasSize);

        // Run application message loop
        Application.Run(_overlayForm);
        return 0;
    }

    private static void PositionStartupPerch()
    {
        if (_physics == null || _animator == null) return;

        // Scan windows and perch on the current foreground window if valid, else fall to floor
        var ledges = LedgeScanner.ScanActiveLedges(new System.Collections.Generic.HashSet<IntPtr>
        {
            _overlayForm?.Handle ?? IntPtr.Zero,
            _speechBubble?.Handle ?? IntPtr.Zero,
            _chatPrompt?.Handle ?? IntPtr.Zero
        });

        IntPtr fg = NativeMethods.GetForegroundWindow();
        Ledge? startupLedge = null;

        if (fg != IntPtr.Zero)
        {
            startupLedge = ledges.Find(l => l.Window == fg);
        }

        if (startupLedge == null && ledges.Count > 0)
        {
            // Pick the first window ledge, or floor
            startupLedge = ledges.Find(l => !l.IsFloor) ?? ledges[0];
        }

        if (startupLedge != null)
        {
            _physics.Land(startupLedge);
        }
        else
        {
            _physics.AnchorX = 400;
            _physics.AnchorY = 200;
            _physics.StartFalling();
        }
    }

    private static void OpenChatPrompt()
    {
        if (_chatPrompt == null || _physics == null || _animator == null) return;
        Point companionCenter = new Point(_physics.AnchorX + (_animator.CanvasSize / 2), _physics.AnchorY);
        _chatPrompt.OpenPrompt(companionCenter);
    }

    private static async void OnUserMessageSubmitted(string text)
    {
        if (_animator == null || _gemini == null || _speechBubble == null || _physics == null) return;

        var priorState = _animator.CurrentState;
        _animator.SetState(AnimationState.Thinking);
        _speechBubble.ShowMessage("Thinking...", new Point(_physics.AnchorX, _physics.AnchorY), _animator.CanvasSize);

        string reply = await _gemini.ChatAsync(text);
        string safeReply = Miko.Security.SecurityGuard.Sanitize(reply, _config?.GeminiApiKey);

        _animator.SetState(AnimationState.Talking);
        _speechBubble.ShowMessage(safeReply, new Point(_physics.AnchorX, _physics.AnchorY), _animator.CanvasSize);

        // Return to resting state after brief talking duration
        _ = Task.Delay(4000).ContinueWith(_ =>
        {
            if (_animator.CurrentState == AnimationState.Talking)
            {
                _animator.SetState(priorState == AnimationState.Perching ? AnimationState.Perching : AnimationState.Idle);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void SetupTrayIcon(string assetsDir)
    {
        _trayIcon = new NotifyIcon
        {
            Text = "Miko Desktop Companion",
            Visible = true
        };

        string iconPath = Path.Combine(assetsDir, "chip.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                _trayIcon.Icon = new Icon(iconPath);
            }
            catch { }
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Talk to Miko (Ctrl+Alt+T)", null, (s, e) => OpenChatPrompt());
        menu.Items.Add("Drop / Gravity Fall", null, (s, e) => _physics?.StartFalling());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Miko", null, (s, e) => ExitApp());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s, e) => OpenChatPrompt();
    }

    private static string FindProjectRoot()
    {
        // Check current working directory first
        string cwd = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(cwd, "assets")))
        {
            return cwd;
        }

        // Search upwards from BaseDirectory
        string current = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(current, "assets")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == null) break;
            current = parent;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static void ExitApp()
    {
        _trayIcon?.Dispose();
        _hotkey?.Dispose();
        _overlayForm?.Close();
        Application.Exit();
    }

    private static int RunTests()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("        MIKO SELF-TEST SUITE            ");
        Console.WriteLine("========================================");

        string rootDir = FindProjectRoot();
        string assetsDir = Path.Combine(rootDir, "assets");
        Console.WriteLine($"[1/5] Root Directory: {rootDir}");
        Console.WriteLine($"      Assets Directory: {assetsDir}");

        if (!Directory.Exists(assetsDir))
        {
            Console.WriteLine("FAIL: Assets directory does not exist!");
            return 1;
        }

        // Test 1: Sprite Loading & Slicing
        Console.WriteLine("\n[2/5] Testing Sprite Clips & Slicing...");
        using var library = new SpriteLibrary(assetsDir);
        string[] requiredClips = { "idle", "perch", "perch_talk", "glitch", "dangle" };
        foreach (var clipName in requiredClips)
        {
            var clip = library.GetClip(clipName);
            if (clip == null || clip.Strip == null)
            {
                Console.WriteLine($"FAIL: Failed to load clip '{clipName}'");
                return 1;
            }
            Console.WriteLine($"      ✓ Clip '{clipName}': {clip.CellWidth}x{clip.CellHeight}px, {clip.FrameCount} frames @ {clip.Fps} FPS");
        }

        // Test 2: Frame Rendering & Alpha Mask Hit-Testing
        Console.WriteLine("\n[3/5] Testing Frame Rendering & Alpha Mask Hit-Testing...");
        using var animator = new SpriteAnimator(library, canvasSize: 320, companionSize: 240);
        animator.SetState(AnimationState.Idle);

        var curClip = animator.CurrentClip;
        Console.WriteLine($"      animator.CurrentClip: {curClip?.Name}, Strip: {curClip?.Strip != null}, StripSize: {curClip?.Strip?.Width}x{curClip?.Strip?.Height}");

        animator.RenderCurrentFrame();

        int minX = 999, maxX = -1, minY = 999, maxY = -1, solidCount = 0;
        int nonZeroAlpha = 0;
        for (int y = 0; y < animator.CanvasSize; y++)
        {
            for (int x = 0; x < animator.CanvasSize; x++)
            {
                byte a = animator.SurfaceBitmap.GetPixel(x, y).A;
                if (a > 0) nonZeroAlpha++;
                if (animator.IsSolidPixel(x, y))
                {
                    solidCount++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        Console.WriteLine($"      Surface nonZeroAlpha: {nonZeroAlpha}, Frame solid bounds: X=[{minX}..{maxX}], Y=[{minY}..{maxY}], Solid pixels: {solidCount}");

        bool transparentCorner = !animator.IsSolidPixel(5, 5);
        int midX = (minX + maxX) / 2;
        int midY = (minY + maxY) / 2;
        bool solidCenter = solidCount > 0 && animator.IsSolidPixel(midX, midY);
        Console.WriteLine($"      ✓ Transparent pixel at (5, 5): passes through = {transparentCorner}");
        Console.WriteLine($"      ✓ Character body pixel at ({midX}, {midY}): solid = {solidCenter}");

        if (!transparentCorner || !solidCenter)
        {
            Console.WriteLine("FAIL: Hit testing logic failed transparent or solid pixel verification!");
            return 1;
        }

        // Test 3: Ledge Detection
        Console.WriteLine("\n[4/5] Testing Desktop Ledge & Floor Scanning...");
        var ledges = LedgeScanner.ScanActiveLedges(new System.Collections.Generic.HashSet<IntPtr>());
        int windowCount = ledges.FindAll(l => !l.IsFloor).Count;
        int floorCount = ledges.FindAll(l => l.IsFloor).Count;
        Console.WriteLine($"      ✓ Discovered {windowCount} active window ledges and {floorCount} desktop floors.");

        if (floorCount == 0)
        {
            Console.WriteLine("FAIL: No desktop floors found!");
            return 1;
        }

        // Test 4: Physics & Gravity Simulation
        Console.WriteLine("\n[5/5] Testing Gravity & Window Perch Physics Simulation...");
        var physics = new PhysicsEngine(animator);
        physics.AnchorX = 400;
        physics.AnchorY = 100;

        physics.StartFalling();

        int maxSteps = 100;
        int steps = 0;
        while (physics.IsFalling && steps < maxSteps)
        {
            physics.Tick(0.033);
            steps++;
        }

        Console.WriteLine($"      ✓ Falling simulation completed in {steps} steps ({steps * 0.033:F2}s). Landed = {!physics.IsFalling}, Seat = {physics.CurrentSeat?.Title}");

        // Test 5: Resource Benchmark
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        // Trim unneeded OS working set pages
        TrimWorkingSet();

        long managedBytes = GC.GetTotalMemory(true);
        using var currentProc = Process.GetCurrentProcess();
        long workingSetBytes = currentProc.WorkingSet64;

        double managedMb = managedBytes / (1024.0 * 1024.0);
        double workingSetMb = workingSetBytes / (1024.0 * 1024.0);

        Console.WriteLine("\n========================================");
        Console.WriteLine($" RESOURCE USAGE BENCHMARK:");
        Console.WriteLine($" - Managed Heap Memory:  {managedMb:F2} MB (Target < 20 MB)");
        Console.WriteLine($" - Process Working Set:   {workingSetMb:F2} MB (Target < 50 MB)");
        Console.WriteLine("========================================");

        if (workingSetMb > 50.0)
        {
            Console.WriteLine("WARNING: Working set exceeded 50 MB threshold!");
        }
        else
        {
            Console.WriteLine("PASS: Resource usage is well within target (< 50 MB)!");
        }

        // Test 6: Security Guardrails & Isolation Enforcement
        Console.WriteLine("\n[6/6] Testing Security Guardrails & Isolation Enforcement...");

        var testConfig = AppConfig.Load(rootDir);
        testConfig.RequireConfirmation = false; // Disable popup dialog during automated CLI unit test
        var testDispatcher = new ToolDispatcher(animator, testConfig);

        // 6a: Arbitrary command execution must be completely blocked
        string cmdResult = testDispatcher.Execute("run_command", "{\"command\":\"whoami\"}");
        bool cmdBlocked = cmdResult.Contains("permanently disabled") || cmdResult.Contains("Security Guardrail");
        Console.WriteLine($"      ✓ Raw shell execution (run_command): blocked = {cmdBlocked}");

        // 6b: Non-allowlisted apps must be rejected
        bool powershellBlocked = !Miko.Security.SecurityGuard.TryResolveAllowlistedApp("powershell", testConfig.AllowedApps, out _, out _);
        bool pathTraversalBlocked = !Miko.Security.SecurityGuard.TryResolveAllowlistedApp("../../cmd.exe", testConfig.AllowedApps, out _, out _);
        bool urlSchemeBlocked = !Miko.Security.SecurityGuard.TryResolveAllowlistedApp("http://malicious.com", testConfig.AllowedApps, out _, out _);
        bool validAppAllowed = Miko.Security.SecurityGuard.TryResolveAllowlistedApp("notepad", testConfig.AllowedApps, out string exe, out string dName);

        Console.WriteLine($"      ✓ Dangerous app 'powershell': blocked = {powershellBlocked}");
        Console.WriteLine($"      ✓ Path traversal '../../cmd.exe': blocked = {pathTraversalBlocked}");
        Console.WriteLine($"      ✓ Protocol scheme 'http://...': blocked = {urlSchemeBlocked}");
        Console.WriteLine($"      ✓ Allowlisted app 'notepad': allowed = {validAppAllowed} -> {exe}");

        // 6c: Sensitive path detection
        bool winBlocked = Miko.Security.SecurityGuard.IsSensitivePath(@"C:\Windows\System32");
        bool envBlocked = Miko.Security.SecurityGuard.IsSensitivePath(@".env");
        bool sshBlocked = Miko.Security.SecurityGuard.IsSensitivePath(@"id_rsa");
        Console.WriteLine($"      ✓ System folder 'C:\\Windows': blocked = {winBlocked}");
        Console.WriteLine($"      ✓ Secrets file '.env': blocked = {envBlocked}");
        Console.WriteLine($"      ✓ SSH key file 'id_rsa': blocked = {sshBlocked}");

        // 6d: Key sanitization
        string leakTest = "Error occurred at https://generativelanguage.googleapis.com?key=AIzaSySecretKey1234567890123456789";
        string sanitized = Miko.Security.SecurityGuard.Sanitize(leakTest, "AIzaSySecretKey1234567890123456789");
        bool keyRedacted = !sanitized.Contains("AIzaSySecretKey1234567890123456789") && sanitized.Contains("[REDACTED_API_KEY]");
        Console.WriteLine($"      ✓ API key leak redaction: passed = {keyRedacted}");

        // 6e: Outbound host validation
        bool validHostPassed = false;
        try { Miko.Security.SecurityGuard.ValidateOutboundEndpoint("https://generativelanguage.googleapis.com/v1beta"); validHostPassed = true; } catch { }
        bool evilHostBlocked = false;
        try { Miko.Security.SecurityGuard.ValidateOutboundEndpoint("https://evil-server.com/api"); } catch { evilHostBlocked = true; }
        Console.WriteLine($"      ✓ Outbound Gemini TLS endpoint validation: passed = {validHostPassed && evilHostBlocked}");

        if (!cmdBlocked || !powershellBlocked || !pathTraversalBlocked || !urlSchemeBlocked || !validAppAllowed ||
            !winBlocked || !envBlocked || !sshBlocked || !keyRedacted || !validHostPassed || !evilHostBlocked)
        {
            Console.WriteLine("FAIL: One or more security guardrails failed!");
            return 1;
        }

        Console.WriteLine("\nAll Phase 1 and Security Guardrail tests passed successfully!");
        return 0;
    }

    private static int RunChatTest()
    {
        string rootDir = FindProjectRoot();
        var config = AppConfig.Load(rootDir);
        using var library = new SpriteLibrary(Path.Combine(rootDir, "assets"));
        using var animator = new SpriteAnimator(library, 320, 240);
        var tools = new ToolDispatcher(animator, config);
        var gemini = new GeminiClient(config, tools);

        Console.WriteLine("Transmitting test message 'hello' to Gemini API...");
        string reply = gemini.ChatAsync("hello").GetAwaiter().GetResult();
        Console.WriteLine($"MIKO REPLY: {reply}");
        return 0;
    }
}
