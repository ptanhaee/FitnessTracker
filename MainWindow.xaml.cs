using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FitnessTracker;

public partial class MainWindow : Window
{
    private readonly FitnessStore store;
    private readonly Dictionary<ChartKind, ChartWindow> charts = new();
    private readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTime displayedDay;
    private double progress;
    private const int HistoryPageSize = 15;
    private int historyPage;
    private List<HistoryRow> historyRows = new();

    internal MainWindow(FitnessStore store)
    {
        this.store = store;
        ThemeManager.Initialize();
        InitializeComponent();
        NativeTheme.Apply(this);
        BuildPalettePicker();
        ThemeManager.Changed += PaletteChanged;
        CalorieDate.Text = WeightDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
        WeightPlot.Store = CaloriePlot.Store = ProgressPlot.Store = store;
        WeightPlot.ExpandRequested += () => OpenChart(ChartKind.Weight);
        CaloriePlot.ExpandRequested += () => OpenChart(ChartKind.Calories);
        ProgressPlot.ExpandRequested += () => OpenChart(ChartKind.Progress);
        store.Changed += Refresh;
        dayTimer.Tick += (_, _) =>
        {
            if (displayedDay == DateTime.Today) return;
            var previous = displayedDay.ToString("yyyy-MM-dd");
            if (CalorieDate.Text == previous) CalorieDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
            if (WeightDate.Text == previous) WeightDate.Text = DateTime.Today.ToString("yyyy-MM-dd");
            Refresh();
        };
        dayTimer.Start();
        Closed += (_, _) => { dayTimer.Stop(); store.Changed -= Refresh; ThemeManager.Changed -= PaletteChanged; };
        Refresh();
    }

    private void BuildPalettePicker()
    {
        foreach (var palette in ThemeManager.Store.Palettes)
        {
            // Swatches show each palette's own colors: background, accent, H, L.
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            foreach (var key in new[] { "BackgroundBrush", "AccentBrush", "HighBrush", "LowBrush" })
                swatches.Children.Add(new Border
                {
                    Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 2, 0), BorderThickness = new Thickness(1),
                    BorderBrush = ThemeManager.ToBrush(palette.Colors.GetValueOrDefault("BorderBrush"), "#808080"),
                    Background = ThemeManager.ToBrush(palette.Colors.GetValueOrDefault(key), "#808080")
                });
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatches);
            content.Children.Add(new TextBlock { Text = palette.Name, VerticalAlignment = VerticalAlignment.Center });
            var chip = new RadioButton { Content = content, GroupName = "ColorPalette", IsChecked = ReferenceEquals(palette, ThemeManager.Store.Current), ToolTip = $"Use the {palette.Name} colors" };
            chip.SetResourceReference(StyleProperty, "PaletteChip");
            System.Windows.Automation.AutomationProperties.SetName(chip, $"{palette.Name} color palette");
            chip.Checked += (_, _) => ThemeManager.Select(palette);
            PalettePicker.Children.Add(chip);
        }
    }

    private void PaletteChanged()
    {
        Refresh();
        foreach (Window window in Application.Current.Windows) NativeTheme.Update(window);
    }

    internal void Refresh()
    {
        displayedDay = DateTime.Today;
        TodayLabel.Text = displayedDay.ToString("dddd, MMMM d, yyyy");
        var daily = store.DailyTotals(displayedDay);
        var week = FitnessStore.WeekStart(displayedDay);
        decimal Sum(DateTime start, DateTime end) => daily.Where(p => p.Key >= start && p.Key <= end).Sum(p => p.Value);
        var current = Sum(week, displayedDay);
        var previous = Sum(week.AddDays(-7), week.AddDays(-1));
        var total = daily.Values.Sum() + store.Data.LegacyCalories;
        SetMetric(ThisWeekValue, current);
        SetMetric(LastWeekValue, previous);
        SetMetric(TotalValue, total);
        ThisWeekAverage.Text = $"{FitnessStore.Signed(current / ((displayedDay - week).Days + 1))} kcal/day average";
        LastWeekAverage.Text = $"{FitnessStore.Signed(previous / 7m)} kcal/day average";
        ThisWeekWeight.Text = FitnessStore.EstimatedWeight(current);
        LastWeekWeight.Text = FitnessStore.EstimatedWeight(previous);
        TotalWeight.Text = FitnessStore.EstimatedWeight(total);
        ThisWeekDates.Text = $"{week:MMM d} – {displayedDay:MMM d}";
        LastWeekDates.Text = $"{week.AddDays(-7):MMM d} – {week.AddDays(-1):MMM d}";
        TrackedAverage.Text = daily.Count == 0 ? "No calorie entries yet" : $"{FitnessStore.Signed(daily.Values.Sum() / daily.Count)} kcal / logged day";
        TrackingSince.Text = $"Tracking since {store.Archive.StartDate:MMM d, yyyy}";
        GoalProgressText.Text = $"{total / store.Goals.CalorieGoal:P1} of {store.Goals.CalorieGoal:N0} kcal goal";
        GoalRemainingText.Text = total <= store.Goals.CalorieGoal ? $"{store.Goals.CalorieGoal - total:N0} kcal to go" : $"{total - store.Goals.CalorieGoal:N0} kcal beyond goal";
        progress = Math.Clamp((double)(total / store.Goals.CalorieGoal), 0, 1);
        UpdateProgress();
        var weight = store.LatestWeight(displayedDay);
        GoalWeight.Text = $"{store.Goals.TargetWeightKg:0.##} kg";
        GoalWeightNote.Text = $"Target by {store.Goals.TargetDate:MMM d, yyyy}";
        var days = weight == null ? 0 : (store.Goals.TargetDate - weight.Date.Date).Days;
        WeightGoalSummary.Text = weight == null ? "Record a weigh-in to get started" :
            $"{Math.Max(0m, weight.WeightKg - store.Goals.TargetWeightKg):0.##} kg to go from {weight.WeightKg:0.##} kg ({weight.Type}, {weight.Date:MMM d})";
        RequiredDeficit.Text = weight == null ? "Add a weight for your daily estimate." : days <= 0 ? "The target date has passed this weigh-in." :
            $"{Math.Max(0m, weight.WeightKg - store.Goals.TargetWeightKg) * FitnessStore.CaloriesPerKg / days:N0} kcal/day to target";
        historyRows = store.AllCalories.Select((e, i) => new HistoryRow(e.Date.Date, i, e.Calories >= 0 ? "Calorie deficit" : "Calorie surplus", $"{FitnessStore.Signed(e.Calories)} kcal", e.Calories >= 0 ? FitnessChart.Mint : FitnessChart.Rose, e, null))
            .Concat(store.Weights.Entries.Select((e, i) => new HistoryRow(e.Date.Date, i, $"Weigh-in · {e.Type}", $"{e.WeightKg:0.##} kg", e.Type.Equals("H", StringComparison.OrdinalIgnoreCase) ? FitnessChart.Orange : FitnessChart.Blue, null, e)))
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Index).ToList();
        ShowHistoryPage();
        WeightPlot.Redraw(); CaloriePlot.Redraw(); ProgressPlot.Redraw();
        foreach (var window in charts.Values) window.Refresh();
    }

    private static void SetMetric(TextBlock text, decimal value) { text.Text = $"{FitnessStore.Signed(value)} kcal"; text.SetResourceReference(TextBlock.ForegroundProperty, value >= 0 ? "AccentBrush" : "NegativeBrush"); }
    private void UpdateProgress() => ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * progress);
    private void ProgressTrackChanged(object sender, SizeChangedEventArgs e) => UpdateProgress();
    private void ExpandWeight(object sender, RoutedEventArgs e) => OpenChart(ChartKind.Weight);
    private void ExpandCalories(object sender, RoutedEventArgs e) => OpenChart(ChartKind.Calories);
    private void ExpandProgress(object sender, RoutedEventArgs e) => OpenChart(ChartKind.Progress);
    internal ChartWindow OpenChart(ChartKind kind)
    {
        if (charts.TryGetValue(kind, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return existing;
        }
        var window = new ChartWindow(store, kind) { Owner = this };
        charts.Add(kind, window);
        window.Closed += (_, _) => charts.Remove(kind);
        window.Show();
        return window;
    }
    private void CalorieKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SaveCalories(sender, e); e.Handled = true; } }
    private void WeightKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SaveWeight(sender, e); e.Handled = true; } }
    private static DateTime ParseDate(string input)
    {
        if (!DateTime.TryParseExact(input.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new ArgumentException("Use a valid date in YYYY-MM-DD format.");
        if (date > DateTime.Today) throw new ArgumentException("The entry date cannot be in the future.");
        return date;
    }
    private void SaveCalories(object sender, RoutedEventArgs e) => SaveEntry(CalorieStatus, () =>
    {
        if (!FitnessStore.TryNumber(CalorieInput.Text.Trim(), out var value)) throw new ArgumentException("Enter calories, for example 350 or -150.");
        var date = ParseDate(CalorieDate.Text);
        store.AddCalories(value, date);
        CalorieInput.Clear(); CalorieInput.Focus();
        return $"Saved {FitnessStore.Signed(value)} kcal · {date:MMM d}";
    });
    private void SaveWeight(object sender, RoutedEventArgs e) => SaveEntry(WeightStatus, () =>
    {
        if (!FitnessStore.TryNumber(WeightInput.Text.Trim(), out var value) || value <= 0) throw new ArgumentException("Enter a positive weight in kilograms.");
        var date = ParseDate(WeightDate.Text);
        var type = HighType.IsChecked == true ? "H" : "L";
        store.AddWeight(value, type, date);
        WeightInput.Clear(); WeightInput.Focus();
        return $"Saved {value:0.##} kg ({type}) · {date:MMM d}";
    });
    private void SaveEntry(TextBlock status, Func<string> action)
    {
        try { status.Text = action(); status.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or OverflowException)
        {
            status.Text = exception is ArgumentException ? exception.Message : $"Could not save. {exception.Message}";
            status.SetResourceReference(TextBlock.ForegroundProperty, "NegativeBrush");
            Refresh();
        }
    }
    // Only page buttons jump to the history list; refreshes (including startup) leave the dashboard at the top.
    private void ShowHistoryPage(bool bringIntoView = false)
    {
        var pages = Math.Max(1, (historyRows.Count + HistoryPageSize - 1) / HistoryPageSize);
        historyPage = Math.Clamp(historyPage, 0, pages - 1);
        HistoryList.ItemsSource = historyRows.Skip(historyPage * HistoryPageSize).Take(HistoryPageSize).ToList();
        HistoryCount.Text = $"{historyRows.Count:N0} entries";
        EmptyHistory.Visibility = historyRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPageLabel.Text = historyRows.Count == 0 ? "Page 1 of 1 · No entries" : $"Page {historyPage + 1} of {pages} · {historyPage * HistoryPageSize + 1}–{Math.Min((historyPage + 1) * HistoryPageSize, historyRows.Count)} of {historyRows.Count}";
        PreviousPageButton.IsEnabled = historyPage > 0;
        NextPageButton.IsEnabled = historyPage < pages - 1;
        if (bringIntoView && HistoryList.Items.Count > 0) HistoryList.ScrollIntoView(HistoryList.Items[0]);
    }
    private void PreviousHistoryPage(object sender, RoutedEventArgs e) { historyPage--; ShowHistoryPage(true); }
    private void NextHistoryPage(object sender, RoutedEventArgs e) { historyPage++; ShowHistoryPage(true); }
    private void EditHistoryEntry(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HistoryRow row) return;
        var dialog = new EditEntryWindow(store, row.Calories, row.Weight) { Owner = this };
        if (dialog.ShowDialog() == true) SetHistoryStatus("Entry updated.");
    }
    private void DeleteHistoryEntry(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HistoryRow row) return;
        var dialog = new DeleteEntryWindow(store, row.Calories, row.Weight, $"{row.Kind}\n{row.DateText} · {row.Value}") { Owner = this };
        if (dialog.ShowDialog() == true) SetHistoryStatus("Entry deleted.");
    }
    private void EditGoals(object sender, RoutedEventArgs e) => new EditGoalsWindow(store) { Owner = this }.ShowDialog();
    private void SetHistoryStatus(string message) { HistoryStatus.Text = message; HistoryStatus.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); HistoryStatus.Visibility = Visibility.Visible; }
    private sealed record HistoryRow(DateTime Date, int Index, string Kind, string Value, Brush Color, CalorieEntry? Calories, WeightEntry? Weight)
    {
        public string DateText => Date.ToString("MMM d, yyyy");
    }
}

internal static class NativeTheme
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    internal static void Apply(Window window) => window.SourceInitialized += (_, _) => Update(window);

    // Dark title bar, tinted to the palette background on Windows 11 (older Windows ignores the tint).
    internal static void Update(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (ThemeManager.Brush("BackgroundBrush") is SolidColorBrush background)
        {
            var c = background.Color;
            var caption = c.R | (c.G << 8) | (c.B << 16);
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        }
    }
}
