using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FitnessTracker;

public partial class WeeklyCaloriesWindow : Window
{
    private const int PageSize = 12;
    private readonly FitnessStore store;
    private IReadOnlyList<WeeklyCalorieSummary> weeks = Array.Empty<WeeklyCalorieSummary>();
    private int page;

    internal WeeklyCaloriesWindow(FitnessStore store)
    {
        this.store = store;
        InitializeComponent();
        NativeTheme.Apply(this);
        WeeklyPlot.Store = store;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Refresh();
    }

    // MainWindow refreshes this owned window on saves, palette changes and day rollover.
    internal void Refresh()
    {
        var anchor = page > 0 ? weeks.Skip(page * PageSize).FirstOrDefault()?.Start : null;
        weeks = WeeklyCalorieData.Create(store, DateTime.Today);
        if (anchor is { } start)
        {
            var index = weeks.ToList().FindIndex(w => w.Start == start);
            if (index >= 0) page = index / PageSize;
        }
        ShowPage();
    }

    private void ShowPage()
    {
        var pages = Math.Max(1, (weeks.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var visible = weeks.Skip(page * PageSize).Take(PageSize).ToArray();
        WeeksList.ItemsSource = visible.Select(w => new WeeklyRow(w)).ToArray();
        WeeklyPlot.WeeklyValues = visible.Reverse().ToArray();
        WeeklyPlot.Redraw();
        ChartRange.Text = visible.Length == 0 ? "Weekly net balance" : $"{visible[^1].Start:MMM d, yyyy} – {visible[0].End:MMM d, yyyy} · Oldest to newest";
        PageLabel.Text = weeks.Count == 0 ? "0 weeks" : $"Weeks {page * PageSize + 1}–{page * PageSize + visible.Length} of {weeks.Count} · Page {page + 1} of {pages}";
        NewerButton.IsEnabled = page > 0;
        OlderButton.IsEnabled = page < pages - 1;
        EmptyMessage.Visibility = weeks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NewerWeeks(object sender, RoutedEventArgs e) { page--; ShowPage(); }
    private void OlderWeeks(object sender, RoutedEventArgs e) { page++; ShowPage(); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private sealed record WeeklyRow(WeeklyCalorieSummary Week)
    {
        public string DateText => $"{Week.Start:MMM d, yyyy} – {Week.End:MMM d, yyyy}";
        public string Status => Week.LoggedDays == 0 ? (Week.IsCurrent ? "This week · No entries" : "No entries") :
            Week.IsCurrent ? "This week · In progress" : Week.LoggedDays < 7 ? "Partially logged" : "Fully logged";
        public string TotalText => Week.LoggedDays == 0 ? "—" : FitnessStore.Signed(Week.Total);
        public string AverageText => Week.LoggedDays == 0 ? "—" : FitnessStore.Signed(Week.DailyAverage);
        public string LoggedText => $"{Week.LoggedDays} / {Week.ElapsedDays}";
        public string WeightText => Week.LoggedDays == 0 ? "—" : FitnessStore.EstimatedWeight(Week.Total);
        public Brush Color => ThemeManager.Brush(Week.LoggedDays == 0 ? "MutedBrush" : Week.Total < 0 ? "NegativeBrush" : "AccentBrush");
    }
}
