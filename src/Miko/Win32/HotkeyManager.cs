using System;
using System.Windows.Forms;

namespace Miko.Win32;

public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x9001;
    public event Action? HotkeyPressed;
    private bool _isRegistered = false;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public bool Register(uint modifiers, Keys key)
    {
        if (_isRegistered) Unregister();

        _isRegistered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _isRegistered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}
