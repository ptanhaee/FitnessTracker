internal readonly record struct WeightPoint(DateTime Date, double Kilograms);

internal sealed record WeightChartData(
    IReadOnlyList<WeightPoint> High,
    IReadOnlyList<WeightPoint> Low,
    IReadOnlyList<WeightPoint> Midpoint)
{
    public static WeightChartData Create(IEnumerable<WeightEntry> entries, DateTime today)
    {
        // Match the report: the last recorded entry wins for a given date/type.
        var recorded = entries.Where(e => e.Date.Date <= today.Date && e.WeightKg > 0).ToList();
        var high = Series("H");
        var low = Series("L");
        var midpoint = new List<WeightPoint>();

        if (high.Count > 0 && low.Count > 0)
        {
            var start = high[0].Date > low[0].Date ? high[0].Date : low[0].Date;
            var end = high[^1].Date < low[^1].Date ? high[^1].Date : low[^1].Date;
            foreach (var date in high.Concat(low).Select(p => p.Date).Distinct().OrderBy(d => d)
                         .Where(d => d >= start && d <= end))
            {
                midpoint.Add(new WeightPoint(date, (Interpolate(high, date) + Interpolate(low, date)) / 2));
            }
        }

        return new WeightChartData(high, low, midpoint);

        List<WeightPoint> Series(string type) => recorded
            .Where(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Date.Date)
            .OrderBy(g => g.Key)
            .Select(g => new WeightPoint(g.Key, (double)g.Last().WeightKg))
            .ToList();
    }

    private static double Interpolate(IReadOnlyList<WeightPoint> points, DateTime date)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].Date == date)
                return points[i].Kilograms;
            if (points[i].Date > date && i > 0)
            {
                var left = points[i - 1];
                var right = points[i];
                var fraction = (date - left.Date).TotalDays / (right.Date - left.Date).TotalDays;
                return left.Kilograms + fraction * (right.Kilograms - left.Kilograms);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(date), "Cannot interpolate outside recorded dates.");
    }
}
