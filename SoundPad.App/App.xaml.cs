using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SoundPad.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, false);
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#3B82F6"),
            ApplicationTheme.Dark);
    }
}
