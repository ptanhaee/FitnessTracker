using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using FitnessTracker;

internal static class Checks
{
    private static int passed;
    [STAThread]
    private static int Main()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/Debug/net8.0/"));
        var artifacts = Path.Combine(root, "Verification");
        Directory.CreateDirectory(artifacts);
        var fixture = Path.Combine(artifacts, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var today = DateTime.Today;
            var empty = new FitnessStore(fixture);
            Check(empty.DailyTotals(today).Count == 0, "Empty history loads without creating files");
            Check(Directory.GetFiles(fixture).Length == 0, "Loading is read-only");
            File.WriteAllText(Path.Combine(fixture, "FitnessSettings.txt"), "100");
            var legacy = new FitnessStore(fixture);
            Check(legacy.Data.LegacyCalories == 100, "Legacy balance imported");
            legacy.AddCalories(250, today);
            legacy.AddCalories(-75, today);
            legacy.AddCalories(50, today.AddDays(-30));
            var reloaded = new FitnessStore(fixture);
            Check(reloaded.DailyTotals(today)[today] == 175 && reloaded.AllCalories.Sum(e => e.Calories) == 225, "Signed and backdated entries survive reload");
            Check(reloaded.Archive.Entries.Count == 1 && reloaded.Data.Entries.Count == 2, "Old entries are archived");
            Check(File.Exists(Path.Combine(fixture, "FitnessData.json.bak")), "Replaced history has a backup");
            reloaded.AddWeight(95, "H", today.AddDays(-2));
            reloaded.AddWeight(93, "L", today);
            reloaded.AddWeight(94, "L", today);
            Check(new FitnessStore(fixture).LatestWeight(today)!.WeightKg == 94, "Last same-day measurement wins");
            Throws(() => reloaded.AddWeight(0, "L", today), "Zero weight rejected");
            Throws(() => reloaded.AddWeight(95, "L", today.AddDays(1)), "Future weight rejected");
            Throws(() => reloaded.AddCalories(50, today.AddDays(1)), "Future calorie date rejected");

            WeightEntry E(int days, decimal kg, string type) => new() { Date = today.AddDays(days), WeightKg = kg, Type = type };
            var interpolated = WeightChartData.Create(new[] { E(-10, 100, "H"), E(0, 90, "H"), E(-8, 90, "L"), E(-2, 84, "L") }, today);
            Check(interpolated.Midpoint.Select(p => p.Kilograms).SequenceEqual(new[] { 94d, 88d }), "H/L interpolation remains correct");
            var duplicate = WeightChartData.Create(new[] { E(0, 94, "l"), E(0, 93, "L"), E(1, 88, "H") }, today);
            Check(duplicate.Low.Single().Kilograms == 93 && duplicate.High.Count == 0 && duplicate.Midpoint.Count == 0, "Duplicate types and future measurements handled");
            Check(WeightChartData.Create(Array.Empty<WeightEntry>(), today).High.Count == 0, "Empty chart data supported");
            Check(FitnessStore.EstimatedWeight(3465).StartsWith("450 g / ") && FitnessStore.EstimatedWeight(-2000).StartsWith("260 g / ") &&
                  FitnessStore.EstimatedWeight(-2000).Contains("gained") && FitnessStore.EstimatedWeight(0).StartsWith("0 g / "),
                "Estimated weight changes under 1 kg show in grams");
            Check(FitnessStore.EstimatedWeight(7697).StartsWith($"{1m:0.00} kg / ") && FitnessStore.EstimatedWeight(-15400).StartsWith($"{2m:0.00} kg / "),
                "Estimated weight changes of 1 kg or more stay in kg, including values that round up to 1 kg");

            var recovery = Path.Combine(fixture, "recovery"); Directory.CreateDirectory(recovery);
            var oldEntry = new CalorieEntry { Date = today.AddDays(-20), Calories = 40 };
            File.WriteAllText(Path.Combine(recovery, "FitnessData.json"), JsonSerializer.Serialize(new FitnessData { Entries = new() { oldEntry, oldEntry } }));
            File.WriteAllText(Path.Combine(recovery, "FitnessArchive.json"), JsonSerializer.Serialize(new FitnessArchive { Entries = new() { oldEntry, oldEntry } }));
            var recovered = new FitnessStore(recovery);
            Check(recovered.AllCalories.Sum(e => e.Calories) == 80, "Interrupted archive recovery preserves duplicate multiplicity");
            recovered.AddCalories(10, today);
            Check(new FitnessStore(recovery).AllCalories.Sum(e => e.Calories) == 90, "Recovery persists without double counting");
            var invalid = Path.Combine(fixture, "invalid"); Directory.CreateDirectory(invalid);
            File.WriteAllText(Path.Combine(invalid, "FitnessData.json"), "broken json");
            Throws(() => new FitnessStore(invalid), "Corrupt history fails safely");
            Check(File.ReadAllText(Path.Combine(invalid, "FitnessData.json")) == "broken json", "Corrupt history is not overwritten");

            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // Rendering uses a copy of the user's real data. UI save tests use only this copy.
            var visual = Path.Combine(fixture, "visual"); Directory.CreateDirectory(visual);
            foreach (var name in new[] { "FitnessData.json", "FitnessArchive.json", "WeightHistory.json" })
                if (File.Exists(Path.Combine(root, name))) File.Copy(Path.Combine(root, name), Path.Combine(visual, name));
            var store = new FitnessStore(visual);
            var paletteDir = Path.Combine(fixture, "palettes"); Directory.CreateDirectory(paletteDir);
            ThemeManager.Store = PaletteStore.Load(paletteDir);
            var deficitPath = Path.Combine(fixture, "daily-deficit"); Directory.CreateDirectory(deficitPath);
            var deficitStore = new FitnessStore(deficitPath);
            deficitStore.Goals.TargetWeightKg = 80;
            deficitStore.Goals.TargetDate = today.AddDays(10);
            deficitStore.AddWeight(95, "H", today);
            var deficitWindow = new MainWindow(deficitStore);
            Check(deficitWindow.RequiredDeficit.Text == "Add an L weight for your daily estimate.", "Daily deficit requires an L weight even when H exists");
            deficitStore.AddWeight(90, "L", today.AddDays(-1));
            deficitStore.AddWeight(91, "H", today);
            Check(deficitWindow.WeightGoalSummary.Text.Contains("from 90 kg") && deficitWindow.RequiredDeficit.Text == $"{7000:N0} kcal/day to target", "Daily deficit uses latest L weight and its date despite a newer H weight");
            deficitStore.AddWeight(89, "L", today.AddDays(-1));
            Check(deficitWindow.WeightGoalSummary.Text.Contains("from 89 kg"), "Daily deficit uses the last L entry on the same day");
            deficitStore.DeleteWeight(deficitStore.Weights.Entries[^1]);
            Check(deficitWindow.WeightGoalSummary.Text.Contains("from 90 kg"), "Deleting the latest L refreshes the daily deficit from the previous L");
            deficitWindow.Close();
            var window = new MainWindow(store); window.Show(); Pump();
            Check(window.DashboardScroll.VerticalOffset == 0, "Dashboard opens scrolled to the top");
            store.AddCalories(0, today); Pump();
            Check(window.DashboardScroll.VerticalOffset == 0, "Saving an entry does not scroll the dashboard");
            store.DeleteCalories(store.Data.Entries[^1]); Pump();
            var renameDir = Path.Combine(fixture, "palette-rename"); Directory.CreateDirectory(renameDir);
            File.WriteAllText(Path.Combine(renameDir, PaletteStore.FileName), JsonSerializer.Serialize(new PaletteSettings { Selected = "Moonlight Midnight", Palettes = new[] { "Moonlight", "Moonlight Midnight", "Space Within", "Space Within Midnight", "Original" }.Select(name => new ColorPalette { Name = name }).ToList() }));
            var renamed = PaletteStore.Load(renameDir);
            Check(renamed.Palettes.Select(p => p.Name).SequenceEqual(new[] { "Original", "Light" }) && renamed.Current.Name == "Original" &&
                  PaletteStore.Load(renameDir).Current.Name == "Original", "Retired palettes are removed and their selection migrates to Original on reload");
            Render(window, Path.Combine(artifacts, "dashboard.png"), 1440, 960);
            Render(window, Path.Combine(artifacts, "dashboard-small.png"), 1100, 720);
            Check(ThemeManager.Store.Palettes.Select(p => p.Name).SequenceEqual(new[] { "Original", "Light" }) &&
                  ThemeManager.Store.Current.Name == "Original" && File.Exists(Path.Combine(paletteDir, PaletteStore.FileName)), "Original and Light are saved and Original is the default");
            Check(ThemeManager.Store.Palettes.Single(p => p.Name == "Original").Colors["BackgroundBrush"] == "#0C111B" &&
                  ThemeManager.Store.Palettes.Single(p => p.Name == "Original").Colors["AccentBrush"] == "#69E0C2", "Original colors are kept as a palette");
            var chips = window.PalettePicker.Children.OfType<RadioButton>().ToList();
            Check(chips.Count == 2 && chips[0].IsChecked == true, "Footer shows a chip per palette");
            foreach (var chip in chips.Skip(1))
            {
                chip.IsChecked = true; Pump();
                var name = ThemeManager.Store.Current.Name.Replace(" ", "-").ToLowerInvariant();
                Render(window, Path.Combine(artifacts, $"dashboard-{name}.png"), 1440, 960);
            }
            Render(window, Path.Combine(artifacts, "dashboard-light-small.png"), 1100, 720);
            Check(PaletteStore.Load(paletteDir).Current.Name == "Light" && ((SolidColorBrush)window.Background).Color == (Color)ColorConverter.ConvertFromString("#F5F4EF"),
                "Switching palettes recolors the open window and persists");
            chips[0].IsChecked = true; Pump();
            Check(((SolidColorBrush)window.Background).Color == (Color)ColorConverter.ConvertFromString("#0C111B"), "Switching back restores Original");
            window.Width = 1440; window.Height = 960; Pump();
            Check(window.WeightPlot.Children.Count > 0 && window.CaloriePlot.Children.Count > 0 && window.ProgressPlot.Children.Count > 0, "All three charts render in the dashboard");
            var popout = window.OpenChart(ChartKind.Weight); Pump();
            Check(popout.IsVisible && window.IsEnabled && ReferenceEquals(popout, window.OpenChart(ChartKind.Weight)), "Chart pop-out is nonmodal and reuses its window");
            Render(popout, Path.Combine(artifacts, "weight-expanded.png"), 1040, 650);
            var before = store.Weights.Entries.Count;
            window.WeightInput.Text = "invalid";
            window.SaveWeightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.Weights.Entries.Count == before && !string.IsNullOrEmpty(window.WeightStatus.Text), "Invalid form input reports an error without saving");
            window.WeightInput.Text = "92.5";
            window.SaveWeightButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(new FitnessStore(visual).Weights.Entries.Count == before + 1 && window.WeightGoalSummary.Text.Contains("from 92.5 kg"), "Weight form saves and refreshes the dashboard");
            var updatedChart = Find<FitnessChart>(popout)!.Children.OfType<Ellipse>().Any(e => e.ToolTip?.ToString()?.Contains("92.5 kg") == true);
            Check(updatedChart, "Open chart refreshes immediately after a weigh-in");
            var calorieBefore = store.AllCalories.Sum(e => e.Calories);
            window.CalorieInput.Text = "-125";
            window.SaveCaloriesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(new FitnessStore(visual).AllCalories.Sum(e => e.Calories) == calorieBefore - 125, "Calorie form saves signed entries");
            popout.Close();
            var reopened = window.OpenChart(ChartKind.Weight); Check(reopened.IsVisible && !ReferenceEquals(popout, reopened), "Chart closes and reopens"); reopened.Close();
            foreach (var kind in new[] { ChartKind.Calories, ChartKind.Progress }) { var chart = window.OpenChart(kind); Pump(); Check(chart.IsVisible, kind + " pop-out opens"); chart.Close(); }
            MutationChecks.Run(fixture, artifacts, Check);
            WeeklyChecks.Run(fixture, artifacts, Check);
            Check(window.HistoryList.Items.Count == 15, "History shows 15 entries per page");
            var firstPage = window.HistoryList.Items.Cast<object>().ToArray();
            window.NextPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!window.HistoryList.Items.Cast<object>().Intersect(firstPage).Any() && window.PreviousPageButton.IsEnabled, "Next page shows different entries");
            window.PreviousPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.HistoryList.Items.Cast<object>().SequenceEqual(firstPage) && !window.PreviousPageButton.IsEnabled, "Previous page returns to the first entries");
            window.NextPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            window.HistoryList.ScrollIntoView(window.HistoryList.Items[0]); window.UpdateLayout();
            var item = (ListBoxItem)window.HistoryList.ItemContainerGenerator.ContainerFromIndex(0);
            var menu = item.ContextMenu; menu.PlacementTarget = item; menu.IsOpen = true; Pump();
            Check(ReferenceEquals(menu.DataContext, item.DataContext) && menu.Items.Count == 2, "Row context menu targets the clicked entry on page two");
            var menuBitmap = new RenderTargetBitmap((int)menu.ActualWidth, (int)menu.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            menuBitmap.Render(menu); var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap));
            using (var menuFile = File.Create(Path.Combine(artifacts, "entry-menu.png"))) menuEncoder.Save(menuFile);
            var rowData = menu.DataContext;
            var selectedCalories = (CalorieEntry?)rowData.GetType().GetProperty("Calories")!.GetValue(rowData);
            var selectedWeight = (WeightEntry?)rowData.GetType().GetProperty("Weight")!.GetValue(rowData);
            menu.IsOpen = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows.OfType<EditEntryWindow>().Single();
                Render(editor, Path.Combine(artifacts, "edit-entry.png"), 460, 420);
                editor.ValueInput.Text = selectedWeight == null ? "321" : "91.25";
                editor.CommitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(window.HistoryStatus.Text == "Entry updated." && (selectedWeight == null ? !store.AllCalories.Contains(selectedCalories!) : !store.Weights.Entries.Contains(selectedWeight)), "Context Edit opens and saves the selected record");
            // Rebind after refresh, then cancel deletion before confirming a separate deletion.
            window.HistoryList.ScrollIntoView(window.HistoryList.Items[0]); window.UpdateLayout();
            item = (ListBoxItem)window.HistoryList.ItemContainerGenerator.ContainerFromIndex(0);
            menu = item.ContextMenu; menu.PlacementTarget = item; menu.IsOpen = true; Pump(); menu.IsOpen = false;
            var beforeDelete = store.AllCalories.Count() + store.Weights.Entries.Count;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<DeleteEntryWindow>().Single();
                Render(dialog, Path.Combine(artifacts, "delete-entry.png"), 460, 340);
                dialog.DialogResult = false;
            }));
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(store.AllCalories.Count() + store.Weights.Entries.Count == beforeDelete, "Cancel deletion leaves the entry untouched");
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<DeleteEntryWindow>().Single().CommitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(store.AllCalories.Count() + store.Weights.Entries.Count == beforeDelete - 1 && window.HistoryStatus.Text == "Entry deleted.", "Confirmed context deletion removes exactly one entry");
            var goals = new EditGoalsWindow(store) { Owner = window };
            goals.Show(); Pump(); Render(goals, Path.Combine(artifacts, "edit-goals.png"), 460, 510);
            Check(goals.CalorieGoalInput.IsEnabled && goals.WeightGoalInput.IsEnabled, "Both goal fields start unlocked");
            var savedTarget = store.Goals.TargetWeightKg;
            goals.CalorieGoalInput.Text = "50000"; goals.TargetDateInput.Text = today.AddMonths(9).ToString("yyyy-MM-dd");
            Check(goals.Source == EditGoalsWindow.GoalSource.Calories && !goals.WeightGoalInput.IsEnabled, "Typing a calorie goal locks the target weight");
            Check(goals.TryCommit() && store.Goals.CalorieGoal == 50000 && store.Goals.TargetWeightKg == savedTarget && store.Goals.TargetDate == today.AddMonths(9),
                "Calorie goal saves without changing the target weight");
            Check(window.GoalProgressText.Text.Contains("50,000"), "Goal edits immediately update dashboard labels");
            goals.Close();

            var latestLow = store.LatestWeight(today, "L")!;
            var byWeight = new EditGoalsWindow(store) { Owner = window }; byWeight.Show(); Pump();
            byWeight.WeightGoalInput.Text = (latestLow.WeightKg - 1.5m).ToString(CultureInfo.CurrentCulture);
            var expectedCalories = FitnessStore.CaloriesToReach(latestLow.WeightKg, latestLow.WeightKg - 1.5m);
            Check(expectedCalories == 11550 && byWeight.Source == EditGoalsWindow.GoalSource.Weight && !byWeight.CalorieGoalInput.IsEnabled &&
                  byWeight.CalorieGoalInput.Text == "11550" && byWeight.GoalHint.Text.Contains("11,550 kcal to lose"),
                "Typing a target weight locks calories and shows the calories to lose from the latest L weight");
            Render(byWeight, Path.Combine(artifacts, "edit-goals-weight.png"), 460, 560);
            byWeight.WeightGoalInput.Text = "";
            Check(byWeight.Source == EditGoalsWindow.GoalSource.Unchanged && byWeight.CalorieGoalInput.IsEnabled && byWeight.CalorieGoalInput.Text == "50000",
                "Clearing the target weight unlocks and restores the calorie goal");
            byWeight.WeightGoalInput.Text = (latestLow.WeightKg + 1).ToString(CultureInfo.CurrentCulture);
            Check(!byWeight.TryCommit() && store.Goals.CalorieGoal == 50000, "A target weight at or above the latest L weight is rejected without saving");
            byWeight.WeightGoalInput.Text = (latestLow.WeightKg - 1.5m).ToString(CultureInfo.CurrentCulture);
            Check(byWeight.TryCommit() && store.Goals.CalorieGoal == 11550 && store.Goals.TargetWeightKg == latestLow.WeightKg - 1.5m,
                "Target weight saves with its calculated calorie goal");
            Check(window.GoalProgressText.Text.Contains("11,550") && window.GoalWeight.Text == $"{latestLow.WeightKg - 1.5m:0.##} kg", "Weight-based goal updates the dashboard");
            byWeight.Close();
            var noLow = Path.Combine(fixture, "no-low"); Directory.CreateDirectory(noLow);
            File.WriteAllText(Path.Combine(noLow, "WeightHistory.json"), JsonSerializer.Serialize(new WeightHistory { Entries = new() { new WeightEntry { Date = today, WeightKg = 95, Type = "H" } } }));
            var noLowWindow = new EditGoalsWindow(new FitnessStore(noLow)); noLowWindow.WeightGoalInput.Text = "79";
            Check(!noLowWindow.TryCommit() && noLowWindow.Error.Text.Contains("L weigh-in"), "Target weight needs an L weigh-in, not just H");
            noLowWindow.Close();
            var pagingPath = Path.Combine(fixture, "paging"); Directory.CreateDirectory(pagingPath);
            File.WriteAllText(Path.Combine(pagingPath, "WeightHistory.json"), JsonSerializer.Serialize(new WeightHistory { Entries = Enumerable.Range(0, 16).Select(i => new WeightEntry { Date = today.AddDays(-i), WeightKg = 90 + i, Type = "L" }).ToList() }));
            var pagingStore = new FitnessStore(pagingPath); var pagingWindow = new MainWindow(pagingStore); pagingWindow.Show(); Pump();
            pagingWindow.NextPageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(pagingWindow.HistoryList.Items.Count == 1 && !pagingWindow.NextPageButton.IsEnabled, "Last page contains only the remaining entry");
            pagingStore.DeleteWeight(pagingStore.Weights.Entries[^1]);
            Check(pagingWindow.HistoryList.Items.Count == 15 && !pagingWindow.PreviousPageButton.IsEnabled && !pagingWindow.NextPageButton.IsEnabled, "Deleting the final page entry returns to the previous page");
            var scrolling = Find<ScrollViewer>(pagingWindow); scrolling?.ScrollToEnd(); Pump(); Render(pagingWindow, Path.Combine(artifacts, "history-paged.png"), 1440, 960); pagingWindow.Close();
            window.Close();
            var emptyWindow = new MainWindow(new FitnessStore(Path.Combine(fixture, "empty")));
            emptyWindow.Show(); Pump(); Render(emptyWindow, Path.Combine(artifacts, "dashboard-empty.png"), 1440, 960); emptyWindow.Close();
            Check(true, "Empty dashboard renders");
            File.WriteAllText(Path.Combine(artifacts, "results.txt"), $"PASS: {passed} checks. Originals unchanged.\n");
            app.Shutdown();
            Console.WriteLine($"PASS: {passed} checks. Screenshots: {artifacts}");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(artifacts, "failure.txt"), e.ToString()); Console.Error.WriteLine(e); return 1; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS: " + message); }
    private static void Throws(Action action, string message) { try { action(); } catch (Exception e) when (e is ArgumentException or JsonException or InvalidDataException) { Check(true, message); return; } throw new Exception(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var match = Find<T>(VisualTreeHelper.GetChild(parent, i)); if (match != null) return match; }
        return null;
    }
    private static void Render(Window window, string path, int width, int height)
    {
        window.Width = width; window.Height = height; Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
