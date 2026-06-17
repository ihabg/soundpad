using System.Diagnostics;
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

        // Catch any unhandled exception on the UI thread so the process does
        // not silently exit.  This is especially important for exceptions
        // thrown inside Dispatcher.BeginInvoke callbacks, which run outside
        // the MainWindow_Loaded try/catch.
        DispatcherUnhandledException += (_, args) =>
        {
            var ex = args.Exception;
            Debug.WriteLine($"[FATAL] Unhandled dispatcher exception: {ex.GetType().Name}: {ex.Message}");
            Debug.WriteLine($"[FATAL] {ex.StackTrace}");
            args.Handled = true; // keep the process alive so the window stays open

            // Surface the error in the main window's status bar if possible.
            try
            {
                if (MainWindow is MainWindow mw)
                    mw.ShowFatalError($"Unexpected error: {ex.Message}");
            }
            catch { }
        };

        ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, false);
        ApplicationAccentColorManager.Apply(
            (Color)ColorConverter.ConvertFromString("#3B82F6"),
            ApplicationTheme.Dark);
    }
}
