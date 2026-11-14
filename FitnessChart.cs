using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FitnessTracker;

public enum ChartKind { Weight, Calories, Progress }

public sealed class FitnessChart : Canvas
{
    // Colors come from the active palette (see Palettes.cs); charts redraw when the palette changes.
    internal static Brush Mint => ThemeManager.Brush("AccentBrush");
    internal static Brush Rose => ThemeManager.Brush("NegativeBrush");
    internal static Brush Orange => ThemeManager.Brush("HighBrush");
    internal static Brush Blue => ThemeManager.Brush("LowBrush");
    internal static Brush Midpoint => ThemeManager.Brush("MidpointBrush");
    internal static Brush Muted => ThemeManager.Brush("MutedBrush");
    internal static Brush GridBrush => ThemeManager.Brush("GridBrush");
    private FitnessStore? store;
    internal FitnessStore? Store { get => store; set { store = value; Redraw(); } }
    public ChartKind Kind { get; set; }
    internal event Action? ExpandRequested;

    public FitnessChart()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Focusable = true;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => Redraw();
        MouseLeftButtonUp += (_, e) => { ExpandRequested?.Invoke(); e.Handled = true; };
        KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { ExpandRequested?.Invoke(); e.Handled = true; } };
        GotKeyboardFocus += (_, _) => Redraw();
        LostKeyboardFocus += (_, _) => Redraw();
    }
    internal void Redraw()
    {
        Children.Clear();
        if (store == null || ActualWidth < 150 || ActualHeight < 100) return;
        if (IsKeyboardFocused)
        {
            Children.Add(new Rectangle { Width = ActualWidth, Height = ActualHeight, Stroke = Mint, StrokeThickness = 1, RadiusX = 5, RadiusY = 5, IsHitTestVisible = false });
        }
        switch (Kind)
        {
            case ChartKind.Weight: DrawWeight(); break;
            case ChartKind.Calories: DrawCalories(); break;
            case ChartKind.Progress: DrawProgress(); break;
        }
    }

    private void DrawWeight()
    {
        var data = WeightChartData.Create(store!.Weights.Entries, DateTime.Today);
        var all = data.High.Concat(data.Low).ToList();
        if (all.Count == 0) { Empty("Your weight story starts here.", "Add a weigh-in to see your trend."); return; }
        var min = all.Min(p => p.Kilograms);
        var max = all.Max(p => p.Kilograms);
        var padding = Math.Max(0.5, (max - min) * .18);
        var axis = Axes(all.Min(p => p.Date), all.Max(p => p.Date), Math.Max(0, min - padding), max + padding, "kg");
        DrawSeries(data.High.Select(p => (p.Date, p.Kilograms)).ToList(), axis, Orange, "H", "kg");
        DrawSeries(data.Low.Select(p => (p.Date, p.Kilograms)).ToList(), axis, Blue, "L", "kg");
        DrawSeries(data.Midpoint.Select(p => (p.Date, p.Kilograms)).ToList(), axis, Midpoint, "Midpoint (interpolated)", "kg", true);
    }

    private void DrawCalories()
    {
        var daily = store!.DailyTotals(DateTime.Today);
        var first = DateTime.Today.AddDays(-13);
        var values = Enumerable.Range(0, 14).Select(i => (Date: first.AddDays(i), Value: daily.GetValueOrDefault(first.AddDays(i)))).ToList();
        var minimum = Math.Min(0, (double)values.Min(p => p.Value));
        var maximum = Math.Max(0, (double)values.Max(p => p.Value));
        if (minimum == maximum) maximum = 500;
        var range = maximum - minimum;
        var a = Axes(first, DateTime.Today, minimum < 0 ? minimum - range * .15 : 0, maximum + range * .15, "kcal", true);
        var width = Math.Max(3, a.Width / 14 * .58);
        Line(a.Left, a.Y(0), a.Left + a.Width, a.Y(0), Muted, 1);
        foreach (var value in values)
        {
            var x = a.X(value.Date);
            var y = a.Y((double)value.Value);
            var baseline = a.Y(0);
            var color = value.Value >= 0 ? Mint : Rose;
            var rect = new Rectangle { Width = width, Height = Math.Max(2, Math.Abs(y - baseline)), RadiusX = 3, RadiusY = 3, Fill = value.Value == 0 ? Muted : color, IsHitTestVisible = false };
            SetLeft(rect, x - width / 2); SetTop(rect, Math.Min(y, baseline)); Children.Add(rect);
            var hit = new Rectangle { Width = a.Width / 14, Height = a.Height, Fill = Brushes.Transparent,
                ToolTip = $"{value.Date:MMM d, yyyy}\n{(daily.ContainsKey(value.Date) ? FitnessStore.Signed(value.Value) + " kcal" : "No entry recorded")}\n{(value.Value > 0 ? "Deficit" : value.Value < 0 ? "Surplus" : "")}" };
            SetLeft(hit, x - hit.Width / 2); SetTop(hit, a.Top); ToolTipService.SetInitialShowDelay(hit, 0); Children.Add(hit);
        }
    }

    private void DrawProgress()
    {
        var daily = store!.DailyTotals(DateTime.Today);
        if (daily.Count == 0 && store.Data.LegacyCalories == 0) { Empty("See your progress add up.", "Log calories to start your cumulative chart."); return; }
        var earliest = daily.Count > 0 ? daily.Keys.Min() : DateTime.Today;
        var start = store.Archive.StartDate is { } tracked && tracked < earliest ? tracked.Date : earliest;
        var total = (double)store.Data.LegacyCalories;
        var points = new List<(DateTime Date, double Value)> { (start, total) };
        foreach (var day in daily.OrderBy(p => p.Key)) { total += (double)day.Value; points.Add((day.Key, total)); }
        if (points[^1].Date < DateTime.Today) points.Add((DateTime.Today, total));
        var min = Math.Min(0, points.Min(p => p.Value));
        var max = Math.Max(0, points.Max(p => p.Value));
        var padding = Math.Max(100, (max - min) * .15);
        var a = Axes(start, DateTime.Today, min < 0 ? min - padding : 0, max + padding, "kcal");
        var fill = new Polygon { Fill = ThemeManager.Brush("ChartFillBrush"), IsHitTestVisible = false };
        fill.Points.Add(new Point(a.X(points[0].Date), a.Y(0)));
        foreach (var p in points) fill.Points.Add(new Point(a.X(p.Date), a.Y(p.Value)));
        fill.Points.Add(new Point(a.X(points[^1].Date), a.Y(0)));
        Children.Add(fill);
        DrawSeries(points, a, Mint, "Cumulative net deficit", "kcal");
    }

    private PlotAxes Axes(DateTime first, DateTime last, double min, double max, string units, bool bars = false)
    {
        var a = new PlotAxes(first, last, min, max, 60, 25, ActualWidth - 78, ActualHeight - 65, bars);
        Label(units, 6, 3, 45);
        for (var i = 0; i <= 4; i++)
        {
            var value = min + (max - min) * i / 4;
            var y = a.Y(value);
            Line(a.Left, y, a.Left + a.Width, y, GridBrush, 1);
            Label(value.ToString(units == "kg" ? "0.0" : "#,##0", CultureInfo.CurrentCulture), 0, y - 8, 49, TextAlignment.Right);
        }
        var days = (last - first).Days;
        var count = Math.Min(days, Math.Max(1, (int)(a.Width / 88)));
        for (var i = 0; i <= count; i++)
        {
            var date = count == 0 ? first : first.AddDays(Math.Round((double)days * i / count));
            Label(date.ToString("MMM d"), a.X(date) - 34, a.Top + a.Height + 12, 68, TextAlignment.Center);
        }
        return a;
    }

    private void DrawSeries(IReadOnlyList<(DateTime Date, double Value)> points, PlotAxes a, Brush color, string name, string units, bool dashed = false)
    {
        if (points.Count > 1)
        {
            var line = new Polyline { Stroke = color, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
                Points = new PointCollection(points.Select(p => new Point(a.X(p.Date), a.Y(p.Value)))) };
            if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 3 };
            Children.Add(line);
        }
        foreach (var point in points)
        {
            var marker = new Ellipse { Width = 8, Height = 8, Fill = color, Stroke = ThemeManager.Brush("CardBrush"), StrokeThickness = 1.5, IsHitTestVisible = false };
            SetLeft(marker, a.X(point.Date) - 4); SetTop(marker, a.Y(point.Value) - 4); Children.Add(marker);
            var hit = new Ellipse { Width = 20, Height = 20, Fill = Brushes.Transparent, ToolTip = $"{name}\n{point.Date:MMM d, yyyy}\n{point.Value:#,##0.##} {units}" };
            ToolTipService.SetInitialShowDelay(hit, 0);
            SetLeft(hit, a.X(point.Date) - 10); SetTop(hit, a.Y(point.Value) - 10); Children.Add(hit);
        }
    }
    private void Empty(string title, string note)
    {
        Label(title, 10, ActualHeight / 2 - 22, ActualWidth - 20, TextAlignment.Center, 15);
        Label(note, 10, ActualHeight / 2 + 6, ActualWidth - 20, TextAlignment.Center);
    }
    private void Line(double x1, double y1, double x2, double y2, Brush color, double thickness) =>
        Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = thickness, IsHitTestVisible = false });
    private void Label(string text, double x, double y, double width, TextAlignment alignment = TextAlignment.Left, double size = 11)
    {
        var label = new TextBlock { Text = text, Foreground = Muted, FontSize = size, Width = width, TextAlignment = alignment, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        SetLeft(label, x); SetTop(label, y); Children.Add(label);
    }
    private sealed record PlotAxes(DateTime First, DateTime Last, double Min, double Max, double Left, double Top, double Width, double Height, bool Bars)
    {
        public double X(DateTime date) => Left + ((Last == First) ? .5 : Bars ? ((date - First).TotalDays + .5) / ((Last - First).TotalDays + 1) : (date - First).TotalDays / (Last - First).TotalDays) * Width;
        public double Y(double value) => Top + (Max - value) / (Max - Min) * Height;
    }
}

internal sealed class ChartWindow : Window
{
    private readonly FitnessChart chart;
    internal ChartWindow(FitnessStore store, ChartKind kind)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        var title = kind switch { ChartKind.Weight => "Weight trend", ChartKind.Calories => "Daily balance", _ => "Cumulative progress" };
        Title = $"Fitness Tracker · {title}";
        Width = 1040; Height = 650; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        NativeTheme.Apply(this);
        var grid = new Grid { Margin = new Thickness(28) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold });
        var legend = new TextBlock { Margin = new Thickness(0, 12, 0, 12) };
        legend.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        void LegendItem(string text, string brush) { var run = new System.Windows.Documents.Run(text); run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brush); legend.Inlines.Add(run); }
        if (kind == ChartKind.Weight)
        {
            LegendItem("● H     ", "HighBrush");
            LegendItem("● L     ", "LowBrush");
            LegendItem("– – Midpoint (interpolated)", "MidpointBrush");
        }
        else if (kind == ChartKind.Calories) { LegendItem("Last 14 days     ", "MutedBrush"); LegendItem("● Deficit (+)     ", "AccentBrush"); LegendItem("● Surplus (−)", "NegativeBrush"); }
        else legend.Text = "All recorded days · Includes your starting balance";
        Grid.SetRow(legend, 1); grid.Children.Add(legend);
        chart = new FitnessChart { Kind = kind, Store = store, Cursor = Cursors.Arrow };
        var border = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = chart };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        Grid.SetRow(border, 2); grid.Children.Add(border);
        var note = new TextBlock { Text = kind == ChartKind.Weight ? "Hover over a point for details. Midpoint uses linear interpolation within overlapping H/L dates.\nFor repeated measurements of the same type on a date, the last entry is shown." : "Hover for details. This chart updates when you save an entry in the dashboard.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontSize = 12 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetRow(note, 3); grid.Children.Add(note);
        Content = grid;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
    internal void Refresh() => chart.Redraw();
}
