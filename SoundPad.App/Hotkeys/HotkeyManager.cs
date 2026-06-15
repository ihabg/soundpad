using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundPad.App.Hotkeys;

// Registers global hotkeys with Windows and raises HotkeyPressed when one is pressed.
// "Global" means the hotkey fires even when another application has focus.
//
// How it works:
//   1. RegisterHotKey() tells Windows to watch for the key combination system-wide.
//   2. When the combo is pressed, Windows posts WM_HOTKEY (0x0312) to this window.
//   3. WndProc() receives that message via an HwndSource hook and fires HotkeyPressed.
//   4. UnregisterHotKey() releases the reservation on window close.
//
// When to create this class:
//   Always create it inside OnSourceInitialized (or later).  That is the earliest
//   moment WPF guarantees that both the HWND and its HwndSource exist.  Creating it
//   in the Window constructor or before SourceInitialized will fail because Handle
//   is zero and HwndSource.FromHwnd returns null.
public class HotkeyManager : IDisposable
{
    // ── Windows API ──────────────────────────────────────────────────────────
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // The Windows message number for hotkey events.
    private const int WmHotkey = 0x0312;

    // ── Modifier key bit-flags (passed to RegisterHotKey's fsModifiers) ───────
    private const uint ModAlt      = 0x0001; // Alt key
    private const uint ModControl  = 0x0002; // Ctrl key
    private const uint ModShift    = 0x0004; // Shift key
    // MOD_NOREPEAT: Windows won't keep sending WM_HOTKEY while the key is held.
    private const uint ModNoRepeat = 0x4000;

    // Pre-built modifier combinations used by this app.
    public static readonly uint CtrlAlt   = ModControl | ModAlt   | ModNoRepeat;
    public static readonly uint CtrlShift = ModControl | ModShift | ModNoRepeat;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly IntPtr         _hwnd;
    private readonly HwndSource     _source;
    private readonly HwndSourceHook _hook;          // stored so RemoveHook has the same reference
    private readonly List<int>      _registeredIds = new List<int>();

    // Fired on the UI thread when a registered hotkey is pressed.
    // The int argument is the ID that was passed to Register().
    public event Action<int>? HotkeyPressed;

    // ── Constructor ──────────────────────────────────────────────────────────
    public HotkeyManager(Window window)
    {
        // EnsureHandle() forces WPF to materialise the Win32 HWND immediately.
        // Reading Handle without EnsureHandle() can return IntPtr.Zero if the
        // window has not been shown yet, which would make RegisterHotKey silently
        // register against handle 0 (ignored by Windows).
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                "Window does not have a Win32 handle. " +
                "Create HotkeyManager from OnSourceInitialized or later.");

        // HwndSource is WPF's bridge that routes Win32 messages into WPF.
        // FromHwnd looks it up by handle.  It returns null when called before
        // SourceInitialized fires, which is why that event is the right place
        // to create this class.
        _source = HwndSource.FromHwnd(_hwnd)
                  ?? throw new InvalidOperationException(
                      "HwndSource.FromHwnd returned null. " +
                      "Make sure HotkeyManager is created from OnSourceInitialized or later.");

        // Store the delegate in a field so we can pass the exact same reference
        // to RemoveHook later.  A new lambda or method-group expression would
        // produce a different object and RemoveHook would silently do nothing.
        _hook = WndProc;
        _source.AddHook(_hook);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    // Asks Windows to reserve this key combination for our window.
    // Returns true on success, false if another process already owns it.
    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        bool ok = RegisterHotKey(_hwnd, id, modifiers, virtualKey);
        if (ok)
            _registeredIds.Add(id);
        return ok;
    }

    // Releases every registered hotkey and detaches the message hook.
    public void Dispose()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_hwnd, id);

        _registeredIds.Clear();
        _source.RemoveHook(_hook);
    }

    // ── Win32 message hook ────────────────────────────────────────────────────
    //
    // HwndSource calls this on the UI thread for every Win32 message the window
    // receives.  We filter for WM_HOTKEY (0x0312) and fire the event.
    // Setting handled = true tells WPF not to pass this message to other hooks.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            // wParam carries the integer ID we passed to RegisterHotKey().
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }
}
