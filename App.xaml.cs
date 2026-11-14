using System.IO;
using System.Windows;

namespace FitnessTracker;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\FitnessTracker.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("Fitness Tracker is already open. Switch to its dashboard in the taskbar.", "Fitness Tracker");
            Shutdown();
            return;
        }
        try
        {
            var store = new FitnessStore(AppContext.BaseDirectory);
            MainWindow = new MainWindow(store);
            MainWindow.Show();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show($"Your saved history could not be loaded. No files were changed.\n\n{exception.Message}", "Unable to open Fitness Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
