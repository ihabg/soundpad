using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundPad.App.Hotkeys;

// Registers global hotkeys with Windows and raises HotkeyPressed when one is pressed.
// "Global" means the hotkey fires even when another application has focus.
//
// How it works:
//   1. RegisterHotKey() tells Windows to watch for the key combination system-wide.
//   2. When the combo is pressed, Windows posts WM_HOTKEY to this window's message queue.
//   3. WndProc() receives that message via the HwndSource hook and fires HotkeyPressed.
//   4. UnregisterHotKey() removes the reservation so other apps can use the key again.
public class HotkeyManager : IDisposable
{
    // ── Windows API ──────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // WM_HOTKEY is the Windows message sent when a registered hotkey is pressed.
    private const int WmHotkey = 0x0312;

    // ── Modifier key constants ────────────────────────────────────────────────
    private const uint ModAlt      = 0x0001;
    private const uint ModControl  = 0x0002;
    // MOD_NOREPEAT prevents WM_HOTKEY from firing over and over while the key is held down.
    private const uint ModNoRepeat = 0x4000;

    // Ready-made combination used by this app: Ctrl + Alt (+ no-repeat).
    public static readonly uint CtrlAlt = ModControl | ModAlt | ModNoRepeat;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly IntPtr          _hwnd;
    private readonly HwndSource      _source;
    private readonly HwndSourceHook  _hook;           // stored so RemoveHook works
    private readonly List<int>       _registeredIds = new();

    // Fired on the UI thread when a registered hotkey is pressed.
    // The int argument is the same ID that was passed to Register().
    public event Action<int>? HotkeyPressed;

    // ── Constructor ──────────────────────────────────────────────────────────
    public HotkeyManager(Window window)
    {
        _hwnd   = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd)
                  ?? throw new InvalidOperationException("Window has no HWND yet.");

        // Keep the delegate in a field — we need the exact same reference to remove the hook later.
        _hook = WndProc;
        _source.AddHook(_hook);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    // Asks Windows to reserve this hotkey for this window.
    // Returns false if another app (or Windows itself) already owns it.
    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        bool ok = RegisterHotKey(_hwnd, id, modifiers, virtualKey);
        if (ok)
            _registeredIds.Add(id);
        return ok;
    }

    // Releases all registered hotkeys and removes the message hook.
    public void Dispose()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_hwnd, id);

        _registeredIds.Clear();
        _source.RemoveHook(_hook);
    }

    // ── Private message pump hook ─────────────────────────────────────────────
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            // wParam is the hotkey ID we passed to RegisterHotKey.
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }
}
