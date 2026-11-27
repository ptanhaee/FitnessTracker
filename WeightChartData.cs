internal readonly record struct WeightPoint(DateTime Date, double Kilograms);

internal sealed record WeightChartData(
    IReadOnlyList<WeightPoint> Low,
    IReadOnlyList<WeightPoint> WeeklyAverage)
{
    public static WeightChartData Create(WeightHistory history, DateTime today)
    {
        var low = history.LowEntries.Where(e => e.Date.Date <= today.Date && e.WeightKg > 0)
            .GroupBy(e => e.Date.Date)
            .OrderBy(day => day.Key)
            .Select(day => new WeightPoint(day.Key, (double)day.Last().WeightKg))
            .ToList();
        var weeklyAverage = low
            .GroupBy(p => FitnessTracker.FitnessStore.WeekStart(p.Date))
            .OrderBy(week => week.Key)
            .Select(week => new WeightPoint(week.Key, week.Average(p => p.Kilograms)))
            .ToList();
        return new WeightChartData(low, weeklyAverage);
    }
}
