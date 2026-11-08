using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace PaperArchive.Native;

public partial class App : Application
{
    // Keep the root window alive for the full process lifetime.
    private Window? _mainWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            WriteStartupDiagnostic("Unhandled UI exception", e.Exception);
            // Keep normal exception handling; never silently swallow potential data errors.
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteStartupDiagnostic("Unhandled runtime exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            WriteStartupDiagnostic("Unobserved background task exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
        catch (Exception error)
        {
            WriteStartupDiagnostic("Application launch failed", error);
            throw;
        }
    }

    private static void WriteStartupDiagnostic(string context, Exception? exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PaperArchive", "logs");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "startup.log");
            File.AppendAllText(file,
                $"{DateTimeOffset.UtcNow:O} | {context}\n{exception}\n\n");
        }
        catch
        {
            // Never trigger a second exception while writing a crash diagnostic.
        }
    }
}
