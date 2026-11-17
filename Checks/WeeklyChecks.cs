using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FitnessTracker;
using Path = System.IO.Path;

internal static class WeeklyChecks
{
    internal static void Run(string fixture, string artifacts, Action<bool, string> check)
    {
        var store = new FitnessStore(Path.Combine(fixture, "weekly-model"));
        var monday = new DateTime(2025, 12, 29);
        var today = monday.AddDays(2);
        store.Data.LegacyCalories = 9000;
        check(WeeklyCalorieData.Create(store, today).Count == 0, "Undated balance alone produces no weekly history");
        store.Archive.Entries.Add(new() { Date = monday.AddDays(-21), Calories = -700 });
        store.Archive.Entries.Add(new() { Date = monday.AddDays(-1), Calories = 350 });
        store.Data.Entries.AddRange(new[] {
            new CalorieEntry { Date = monday, Calories = 100 },
            new CalorieEntry { Date = monday.AddHours(10), Calories = -40 },
            new CalorieEntry { Date = today, Calories = 0 },
            new CalorieEntry { Date = today.AddDays(1), Calories = 999 }
        });
        var weeks = WeeklyCalorieData.Create(store, today);
        check(weeks.Count == 4 && weeks[0].Start == monday && weeks[0].End == today && weeks[0].IsCurrent,
            "Weekly history includes current and missing weeks, newest first, across year boundaries");
        check(weeks[0].Total == 60 && weeks[0].DailyAverage == 20 && weeks[0].LoggedDays == 2 && weeks[0].ElapsedDays == 3,
            "Partial-week average uses elapsed days; duplicates share a logged day, zero entries count, future entries do not");
        check(weeks[1].Total == 350 && weeks[1].DailyAverage == 50 && weeks[1].End == monday.AddDays(-1),
            "Sunday belongs to the prior week and completed weeks use seven calendar days");
        check(weeks[2].LoggedDays == 0 && weeks[3].Total == -700 && weeks.Sum(w => w.Total) == -290,
            "Weekly totals combine archives and active entries, preserve surplus and exclude legacy balance");
        check(WeeklyCalorieData.Create(store, monday)[0].ElapsedDays == 1 && WeeklyCalorieData.Create(store, monday.AddDays(6))[0].ElapsedDays == 7,
            "Monday and Sunday current-week boundaries are correct");

        var path = Path.Combine(fixture, "weekly-ui"); Directory.CreateDirectory(path);
        var liveStore = new FitnessStore(path);
        var liveToday = DateTime.Today;
        var liveMonday = FitnessStore.WeekStart(liveToday);
        for (var i = 0; i < 27; i++)
            if (i != 3) liveStore.Archive.Entries.Add(new() { Date = liveMonday.AddDays(-7 * i), Calories = i == 2 ? 0 : i % 3 == 0 ? -700 : 1400 });
        var owner = new MainWindow(liveStore); owner.Show();
        var history = owner.OpenWeeklyCalories(); Pump();
        check(history.IsVisible && owner.IsEnabled && ReferenceEquals(history, owner.OpenWeeklyCalories()), "Weekly expansion is nonmodal and reuses its open window");
        check(history.WeeksList.Items.Count == 12 && history.OlderButton.IsEnabled && !history.NewerButton.IsEnabled,
            "Weekly history pages twelve weeks at a time");
        check(history.WeeklyPlot.Children.OfType<Rectangle>().Any(r => r.ToolTip?.ToString()?.Contains("No entries recorded") == true) &&
              history.WeeklyPlot.Children.OfType<Rectangle>().Any(r => r.ToolTip?.ToString()?.Contains("Balanced") == true),
            "Chart distinguishes missing weeks from a logged zero");
        Render(history, Path.Combine(artifacts, "weekly-history.png"), 1100, 820);
        Render(history, Path.Combine(artifacts, "weekly-history-small.png"), 900, 680);
        history.OlderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(history.WeeklyPlot.WeeklyValues[^1].Start == liveMonday.AddDays(-84) && history.NewerButton.IsEnabled,
            "Older pages keep the chart and list on the same weeks");
        history.OlderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(history.WeeksList.Items.Count == 3 && !history.OlderButton.IsEnabled, "Oldest weekly page shows the remaining weeks");
        history.NewerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        history.NewerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var before = history.WeeklyPlot.WeeklyValues[^1].Total;
        liveStore.AddCalories(123, liveToday);
        check(history.WeeklyPlot.WeeklyValues[^1].Total == before + 123, "Open weekly history refreshes after saving an entry");
        liveStore.UpdateCalories(liveStore.Data.Entries[^1], -50, liveToday);
        check(history.WeeklyPlot.WeeklyValues[^1].Total == before - 50, "Open weekly history refreshes after editing an entry");
        liveStore.DeleteCalories(liveStore.Data.Entries[^1]);
        check(history.WeeklyPlot.WeeklyValues[^1].Total == before, "Open weekly history refreshes after deleting an entry");
        var originalPalette = ThemeManager.Store.Current;
        ThemeManager.Select(ThemeManager.Store.Palettes.Last()); Pump();
        check(((SolidColorBrush)history.Background).Color == ((SolidColorBrush)ThemeManager.Brush("BackgroundBrush")).Color,
            "Weekly history follows palette changes");
        Render(history, Path.Combine(artifacts, "weekly-history-light.png"), 1100, 820);
        Render(history, Path.Combine(artifacts, "weekly-history-light-small.png"), 900, 680);
        ThemeManager.Select(originalPalette);
        history.Close();
        var reopened = owner.OpenWeeklyCalories();
        check(reopened.IsVisible && !ReferenceEquals(history, reopened), "Weekly window reopens after closing");
        owner.Close();
        check(!reopened.IsVisible, "Closing dashboard closes its weekly history");
        var emptyOwner = new MainWindow(new FitnessStore(Path.Combine(fixture, "weekly-empty"))); emptyOwner.Show();
        var empty = emptyOwner.OpenWeeklyCalories(); Pump();
        check(empty.EmptyMessage.Visibility == Visibility.Visible && !empty.OlderButton.IsEnabled && !empty.NewerButton.IsEnabled,
            "Empty weekly history has a clear message and disabled paging");
        Render(empty, Path.Combine(artifacts, "weekly-history-empty.png"), 900, 680);
        emptyOwner.Close();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Render(Window window, string path, int width, int height)
    {
        window.Width = width; window.Height = height; Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
