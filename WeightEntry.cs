internal sealed class WeightHistory
{
    public List<WeightEntry> Entries { get; set; } = new();
}

internal sealed class WeightEntry
{
    public DateTime Date { get; set; }
    public decimal WeightKg { get; set; }
    public string Type { get; set; } = "L";
}
