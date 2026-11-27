using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FitnessTracker;

internal sealed class FitnessData
{
    public decimal LegacyCalories { get; set; }
    public List<CalorieEntry> Entries { get; set; } = new();
}
internal sealed class FitnessArchive
{
    public DateTime? StartDate { get; set; }
    public List<CalorieEntry> Entries { get; set; } = new();
}
internal sealed class CalorieEntry
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Calories { get; set; }
}

internal sealed class FitnessGoals
{
    public decimal CalorieGoal { get; set; } = 38500m;
    public decimal TargetWeightKg { get; set; } = 80m;
    public DateTime TargetDate { get; set; } = new(2027, 5, 1);
}

internal sealed class FitnessStore
{
    private readonly string directory;
    private bool activeNeedsReconciliation;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public FitnessData Data { get; private set; }
    public FitnessArchive Archive { get; private set; }
    public WeightHistory Weights { get; private set; }
    public FitnessGoals Goals { get; private set; }
    public const decimal CaloriesPerKg = 7700m;
    public event Action? Changed;

    public FitnessStore(string directory)
    {
        this.directory = directory;
        Data = Read<FitnessData>("FitnessData.json");
        Archive = Read<FitnessArchive>("FitnessArchive.json");
        Weights = Read<WeightHistory>("WeightHistory.json");
        Goals = Read<FitnessGoals>("FitnessGoals.json");
        if (Goals.CalorieGoal <= 0 || Goals.TargetWeightKg <= 0)
            throw new InvalidDataException("FitnessGoals.json must contain positive calorie and weight goals.");
        if (Data.Entries == null || Archive.Entries == null || Weights.Entries == null ||
            Data.Entries.Any(x => x == null) || Archive.Entries.Any(x => x == null) || Weights.Entries.Any(x => x == null))
            throw new InvalidDataException("A history file contains an invalid entry list.");
        var legacy = Path.Combine(directory, "FitnessSettings.txt");
        if (!File.Exists(Path.Combine(directory, "FitnessData.json")) && File.Exists(legacy))
        {
            if (!TryNumber(File.ReadAllText(legacy).Trim(), out var value))
                throw new InvalidDataException("FitnessSettings.txt does not contain a valid calorie total.");
            Data.LegacyCalories = value;
        }
        // Recover a partial archive transaction using the same multiplicity rule as the original app.
        var cutoff = DateTime.Today.AddDays(-13);
        var counts = Archive.Entries.GroupBy(e => (e.Id, e.Date.Date, e.Calories)).ToDictionary(g => g.Key, g => g.Count());
        activeNeedsReconciliation = Data.Entries.RemoveAll(e =>
        {
            var key = (e.Id, e.Date.Date, e.Calories);
            if (e.Date.Date >= cutoff || !counts.TryGetValue(key, out var count) || count == 0) return false;
            counts[key] = count - 1;
            return true;
        }) > 0;
        Archive.StartDate ??= AllCalories.Any()
            ? new[] { AllCalories.Min(x => x.Date.Date), DateTime.Today.AddDays(-14) }.Min()
            : DateTime.Today.AddDays(-14);
    }

    public IEnumerable<CalorieEntry> AllCalories => Archive.Entries.Concat(Data.Entries);
    public Dictionary<DateTime, decimal> DailyTotals(DateTime today) => AllCalories
        .Where(e => e.Date.Date <= today.Date).GroupBy(e => e.Date.Date).ToDictionary(g => g.Key, g => g.Sum(e => e.Calories));
    public WeightEntry? LatestWeight(DateTime today) => Weights.LowEntries
        .Where(e => e.Date.Date <= today.Date).OrderBy(e => e.Date.Date).LastOrDefault();
    // Calories to lose to go from a current weight to a target weight, at 7,700 kcal/kg, rounded to whole kcal.
    public static decimal CaloriesToReach(decimal currentKg, decimal targetKg) =>
        Math.Round((currentKg - targetKg) * CaloriesPerKg, 0, MidpointRounding.AwayFromZero);

    public void AddCalories(decimal value, DateTime date)
    {
        ValidateDate(date);
        var cutoff = DateTime.Today.AddDays(-13);
        // Finish maintenance before appending the new entry. A failed maintenance write can
        // therefore never report a failed new entry that was actually already persisted.
        var old = Data.Entries.Where(e => e.Date.Date < cutoff).ToList();
        if (old.Count > 0)
        {
            var archived = new FitnessArchive { StartDate = Archive.StartDate, Entries = Archive.Entries.Concat(old).ToList() };
            Save("FitnessArchive.json", archived);
            Archive = archived;
            Data.Entries = Data.Entries.Where(e => e.Date.Date >= cutoff).ToList();
        }
        Save("FitnessData.json", Data);
        activeNeedsReconciliation = false;
        var entry = new CalorieEntry { Id = Guid.NewGuid().ToString("N"), Date = date.Date, Calories = value };
        if (date.Date < cutoff)
        {
            var next = new FitnessArchive { StartDate = Archive.StartDate, Entries = Archive.Entries.Append(entry).ToList() };
            Save("FitnessArchive.json", next);
            Archive = next;
        }
        else
        {
            // Persist StartDate even for a brand-new history with no archived entries.
            Save("FitnessArchive.json", Archive);
            var next = new FitnessData { LegacyCalories = Data.LegacyCalories, Entries = Data.Entries.Append(entry).ToList() };
            Save("FitnessData.json", next);
            Data = next;
        }
        Changed?.Invoke();
    }

    public void AddWeight(decimal value, DateTime date)
    {
        ValidateDate(date);
        if (value <= 0) throw new ArgumentException("Enter a positive weight in kilograms.");
        var next = new WeightHistory { Entries = Weights.Entries.Append(new WeightEntry { Date = date.Date, WeightKg = value }).ToList() };
        Save("WeightHistory.json", next);
        Weights = next;
        Changed?.Invoke();
    }

    public void UpdateGoals(decimal calories, decimal weight, DateTime targetDate)
    {
        if (calories <= 0 || weight <= 0) throw new ArgumentException("Calorie and weight goals must be positive numbers.");
        var next = new FitnessGoals { CalorieGoal = calories, TargetWeightKg = weight, TargetDate = targetDate.Date };
        Save("FitnessGoals.json", next);
        Goals = next;
        Changed?.Invoke();
    }

    public void UpdateCalories(CalorieEntry entry, decimal value, DateTime date)
    {
        ValidateDate(date);
        ChangeCalories(entry, new CalorieEntry { Id = entry.Id ?? Guid.NewGuid().ToString("N"), Date = date.Date, Calories = value });
    }
    public void DeleteCalories(CalorieEntry entry) => ChangeCalories(entry, null);
    private void ChangeCalories(CalorieEntry entry, CalorieEntry? replacement)
    {
        // Complete any recovered archive write before editing archived entries, otherwise
        // their stale active copies could reappear at the next startup.
        if (activeNeedsReconciliation)
        {
            Save("FitnessData.json", Data);
            activeNeedsReconciliation = false;
        }
        // Keep the owning file for an atomic edit, even when changing the date. Reports use
        // both files. Active entries that become old are archived on the next calorie save.
        if (Data.Entries.Contains(entry))
        {
            var next = new FitnessData { LegacyCalories = Data.LegacyCalories, Entries = Replace(Data.Entries, entry, replacement) };
            Save("FitnessData.json", next);
            Data = next;
        }
        else
        {
            var next = new FitnessArchive { StartDate = Archive.StartDate, Entries = Replace(Archive.Entries, entry, replacement) };
            Save("FitnessArchive.json", next);
            Archive = next;
        }
        Changed?.Invoke();
    }
    public void UpdateWeight(WeightEntry entry, decimal value, DateTime date)
    {
        ValidateDate(date);
        if (value <= 0) throw new ArgumentException("Enter a positive weight in kilograms.");
        ChangeWeight(entry, new WeightEntry { Date = date.Date, WeightKg = value, Type = entry.Type });
    }
    public void DeleteWeight(WeightEntry entry) => ChangeWeight(entry, null);
    private void ChangeWeight(WeightEntry entry, WeightEntry? replacement)
    {
        var next = new WeightHistory { Entries = Replace(Weights.Entries, entry, replacement) };
        Save("WeightHistory.json", next);
        Weights = next;
        Changed?.Invoke();
    }
    private static List<T> Replace<T>(List<T> entries, T original, T? replacement) where T : class
    {
        var index = entries.IndexOf(original);
        if (index < 0) throw new ArgumentException("This entry has changed. Close this dialog and select it again.");
        var next = entries.ToList();
        if (replacement == null) next.RemoveAt(index); else next[index] = replacement;
        return next;
    }

    private static void ValidateDate(DateTime date)
    {
        if (date.Date > DateTime.Today) throw new ArgumentException("The entry date cannot be in the future.");
    }
    private T Read<T>(string name) where T : new()
    {
        var path = Path.Combine(directory, name);
        return !File.Exists(path) ? new T() : JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"{name} contains no history object.");
    }
    private void Save<T>(string name, T value)
    {
        var path = Path.Combine(directory, name);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
    public static bool TryNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out number) ||
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public static string Signed(decimal value) => value.ToString("+#,##0;-#,##0;0", CultureInfo.CurrentCulture);
    public static string EstimatedWeight(decimal calories) => $"{Kilograms(calories / CaloriesPerKg)} / {Math.Abs(calories / CaloriesPerKg * 2.2046226218487757m):0.00} lb {(calories >= 0 ? "lost" : "gained")} (est.)";
    // Amounts under 1 kg show in grams. The cutoff uses the rounded gram value, so 0.9996 kg reads "1.00 kg", never "1000 g".
    public static string Kilograms(decimal kilograms)
    {
        var grams = Math.Round(Math.Abs(kilograms) * 1000m, MidpointRounding.AwayFromZero);
        return grams < 1000m ? $"{grams:0} g" : $"{Math.Abs(kilograms):0.00} kg";
    }
}
