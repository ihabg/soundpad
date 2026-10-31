using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoundPad.App.Hotkeys;















public class HotkeyManager : IHotkeyManager, IDisposable
{
    
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    
    private const int WmHotkey = 0x0312;

    
    private const uint ModAlt      = 0x0001; 
    private const uint ModControl  = 0x0002; 
    private const uint ModShift    = 0x0004; 
    
    private const uint ModNoRepeat = 0x4000;

    
    public static readonly uint CtrlAlt   = ModControl | ModAlt   | ModNoRepeat;
    public static readonly uint CtrlShift = ModControl | ModShift | ModNoRepeat;

    
    private readonly IntPtr         _hwnd;
    private readonly HwndSource     _source;
    private readonly HwndSourceHook _hook;          
    private readonly List<int>      _registeredIds = new List<int>();

    
    
    public event Action<int>? HotkeyPressed;

    
    public HotkeyManager(Window window)
    {
        
        
        
        
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                "Window does not have a Win32 handle. " +
                "Create HotkeyManager from OnSourceInitialized or later.");

        
        
        
        
        _source = HwndSource.FromHwnd(_hwnd)
                  ?? throw new InvalidOperationException(
                      "HwndSource.FromHwnd returned null. " +
                      "Make sure HotkeyManager is created from OnSourceInitialized or later.");

        
        
        
        _hook = WndProc;
        _source.AddHook(_hook);
    }

    

    
    
    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        bool ok = RegisterHotKey(_hwnd, id, modifiers, virtualKey);
        if (ok)
            _registeredIds.Add(id);
        return ok;
    }

    
    
    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            UnregisterHotKey(_hwnd, id);

        _registeredIds.Clear();
    }

    
    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(_hook);
    }

    
    
    
    
    
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }
}
