internal sealed class WeightHistory
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<WeightEntry> LowEntries => Entries.Where(e => string.Equals(e.Type, "L", StringComparison.OrdinalIgnoreCase));

    public List<WeightEntry> Entries { get; set; } = new();
}

internal sealed class WeightEntry
{
    public DateTime Date { get; set; }
    public decimal WeightKg { get; set; }
    public string Type { get; set; } = "L";
}
