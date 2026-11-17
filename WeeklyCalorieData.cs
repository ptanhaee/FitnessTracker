namespace FitnessTracker;

internal sealed record WeeklyCalorieSummary(DateTime Start, DateTime End, decimal Total, int LoggedDays, int ElapsedDays, bool IsCurrent)
{
    public decimal DailyAverage => Total / ElapsedDays;
}

internal static class WeeklyCalorieData
{
    // Calendar-day averages match the dashboard. Missing days are visible in LoggedDays;
    // an undated legacy balance cannot be assigned to a week.
    internal static IReadOnlyList<WeeklyCalorieSummary> Create(FitnessStore store, DateTime today)
    {
        today = today.Date;
        var daily = store.DailyTotals(today);
        if (daily.Count == 0) return Array.Empty<WeeklyCalorieSummary>();
        var current = FitnessStore.WeekStart(today);
        var first = FitnessStore.WeekStart(daily.Keys.Min());
        var groups = daily.GroupBy(p => FitnessStore.WeekStart(p.Key))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.Value), Days: g.Count()));
        var weeks = new List<WeeklyCalorieSummary>();
        for (var start = current; ; start = start.AddDays(-7))
        {
            var end = start == current ? today : start.AddDays(6);
            var totals = groups.GetValueOrDefault(start);
            weeks.Add(new(start, end, totals.Total, totals.Days, (end - start).Days + 1, start == current));
            if (start == first) break;
        }
        return weeks;
    }
}
