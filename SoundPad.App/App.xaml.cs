using SoundPad.App.Services;
using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SoundPad.App;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        StartupLogger.Log("App.Application_Startup begin");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            StartupLogger.Log($"[FATAL AppDomain] {ex?.ToString() ?? args.ExceptionObject?.ToString()}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupLogger.Log($"[FATAL Task] {args.Exception}");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            StartupLogger.Log($"[FATAL Dispatcher] {args.Exception}");
            args.Handled = true;
            try
            {
                if (MainWindow is MainWindow mw)
                    mw.ShowFatalError($"Unexpected error: {args.Exception.Message}");
            }
            catch { }
        };

        StartupLogger.Log("Applying theme");
        try
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, false);
            ApplicationAccentColorManager.Apply(
                (Color)ColorConverter.ConvertFromString("#3B82F6"),
                ApplicationTheme.Dark);
            StartupLogger.Log("Theme applied");
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"Theme FAILED ({ex.GetType().Name}): {ex.Message} — continuing");
        }

        StartupLogger.Log("Creating MainWindow");
        MainWindow window;
        try
        {
            window = new MainWindow();
            StartupLogger.Log("MainWindow created");
        }
        catch (Exception ex)
        {
            StartupLogger.Log($"[FATAL] MainWindow constructor threw: {ex}");
            System.Windows.MessageBox.Show(
                $"SoundPad failed to start:\n\n{ex.Message}\n\nSee startup log:\n{StartupLogger.LogPath}",
                "SoundPad — Startup Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = window;
        window.Show();
        StartupLogger.Log("MainWindow shown");
    }
}
