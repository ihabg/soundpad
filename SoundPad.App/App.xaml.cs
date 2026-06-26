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

        // Initialize rolling log ASAP so runtime errors land in a dated file.
        // Failure is silent — StartupLogger still covers very early fatal failures.
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                              .GetName().Version;
            var versionStr = version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
            AppLogger.Initialize(versionStr);
            AppLogger.Info("Startup", "AppLogger initialized");
        }
        catch { /* cannot log this; StartupLogger is the fallback */ }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = ex?.ToString() ?? args.ExceptionObject?.ToString() ?? "(unknown)";
            StartupLogger.Log($"[FATAL AppDomain] {msg}");
            AppLogger.Error("Crash", "Unhandled AppDomain exception (fatal)", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupLogger.Log($"[FATAL Task] {args.Exception}");
            AppLogger.Error("Crash", "Unobserved Task exception", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            StartupLogger.Log($"[FATAL Dispatcher] {args.Exception}");
            AppLogger.Error("Crash", "Unhandled Dispatcher exception", args.Exception);
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
            AppLogger.Error("Startup", "MainWindow constructor threw — aborting", ex);
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
