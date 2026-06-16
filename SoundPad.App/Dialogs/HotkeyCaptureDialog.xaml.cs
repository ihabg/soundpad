using System.Windows;
using System.Windows.Input;
using SoundPad.App.Models;

namespace SoundPad.App.Dialogs;

// Modal dialog that captures a single global hotkey combination for a sound.
// Key handling is done by overriding OnPreviewKeyDown at the window level
// (tunneling phase), so the capture works no matter which child control has
// logical focus, and Handled=true suppresses any default key behavior
// (e.g. Enter activating a focused button) while capturing.
public partial class HotkeyCaptureDialog : Wpf.Ui.Controls.FluentWindow
{
    public HotkeyBinding? ResultBinding { get; private set; }
    public bool           WasCleared    { get; private set; }

    public HotkeyCaptureDialog(Window owner, string soundName, HotkeyBinding? currentBinding)
    {
        InitializeComponent();
        Owner = owner;
        DialogTitleBar.Title = $"Set Hotkey — {soundName}";

        if (currentBinding is not null)
            CapturedText.Text = currentBinding.DisplayText;

        ResultBinding = currentBinding;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        var modifiers = Keyboard.Modifiers;

        if ((key == Key.Back || key == Key.Delete) && modifiers == ModifierKeys.None)
        {
            WasCleared    = true;
            ResultBinding = null;
            DialogResult  = true;
            return;
        }

        if (IsModifierKey(key))
            return; // wait for a real key while modifiers are held

        if (modifiers == ModifierKeys.None)
        {
            CapturedText.Text   = "Hold Ctrl, Alt, Shift, or Win + a key";
            SaveButton.IsEnabled = false;
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
            return;

        var binding = new HotkeyBinding
        {
            Modifiers   = (uint)modifiers,
            Key         = (uint)vk,
            DisplayText = BuildDisplayText(modifiers, key)
        };

        ResultBinding        = binding;
        CapturedText.Text    = binding.DisplayText;
        SaveButton.IsEnabled = true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private static string BuildDisplayText(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))      parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))    parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))  parts.Add("Win");
        parts.Add(KeyToDisplayString(key));
        return string.Join("+", parts);
    }

    private static string KeyToDisplayString(Key key)
    {
        var name = key.ToString();
        if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
            return name[1].ToString();
        return name;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        WasCleared    = true;
        ResultBinding = null;
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ResultBinding is null)
            return;

        WasCleared   = false;
        DialogResult = true;
    }
}
