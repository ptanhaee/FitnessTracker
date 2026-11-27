using System.IO;
using System.Text.Json;
using FitnessTracker;

internal static class MutationChecks
{
    internal static void Run(string fixture, string artifacts, Action<bool, string> check)
    {
        var path = Path.Combine(fixture, "mutations"); Directory.CreateDirectory(path);
        var today = DateTime.Today;
        var store = new FitnessStore(path);
        store.AddCalories(100, today); store.AddCalories(100, today);
        var first = store.Data.Entries[0]; var second = store.Data.Entries[1];
        store.UpdateCalories(second, -40, today.AddDays(-1));
        check(store.AllCalories.Count() == 2 && store.Data.Entries[0] == first && new FitnessStore(path).AllCalories.Sum(e => e.Calories) == 60, "Editing a duplicate calorie row changes only that row");
        store.DeleteCalories(first);
        check(new FitnessStore(path).AllCalories.Single().Calories == -40, "Deleting calories persists without removing a different row");
        store.AddCalories(77, today.AddDays(-30));
        var archived = store.Archive.Entries.Single();
        store.UpdateCalories(archived, 88, today.AddDays(-29));
        check(new FitnessStore(path).Archive.Entries.Single().Calories == 88, "Archived calorie edits persist");
        store.UpdateCalories(store.Data.Entries.Single(), 88, today.AddDays(-29));
        check(new FitnessStore(path).AllCalories.Count(e => e.Calories == 88) == 2, "Backdating an edit preserves a distinct identical archived entry");
        store.DeleteCalories(store.Archive.Entries.Single());
        check(new FitnessStore(path).AllCalories.Single().Calories == 88, "Archived deletion removes only its selected record");
        store.AddWeight(95, today); store.AddWeight(95, today);
        var originalWeight = store.Weights.Entries[0];
        store.UpdateWeight(store.Weights.Entries[1], 93, today.AddDays(-1));
        var reload = new FitnessStore(path);
        check(reload.Weights.Entries.Count == 2 && reload.Weights.Entries[1].WeightKg == 93 && reload.Weights.Entries[1].Type == "L", "Weight edits preserve the L type and persist the value and date");
        store.DeleteWeight(originalWeight);
        check(new FitnessStore(path).LatestWeight(today)!.WeightKg == 93, "Deleting latest weight reveals the preceding measurement");
        var before = File.ReadAllText(Path.Combine(path, "WeightHistory.json"));
        try { store.UpdateWeight(store.Weights.Entries[0], -1, today); } catch (ArgumentException) { }
        check(File.ReadAllText(Path.Combine(path, "WeightHistory.json")) == before, "Invalid edits do not write history");
        using (File.Open(Path.Combine(path, "WeightHistory.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { store.DeleteWeight(store.Weights.Entries[0]); } catch (IOException) { }
            check(store.Weights.Entries.Count == 1, "Failed disk deletion leaves the in-memory entry intact");
        }
        store.UpdateGoals(42000, 82.5m, today.AddMonths(8));
        reload = new FitnessStore(path);
        check(reload.Goals.CalorieGoal == 42000 && reload.Goals.TargetWeightKg == 82.5m && reload.Goals.TargetDate == today.AddMonths(8), "All goal settings survive restart");
        try { store.UpdateGoals(0, 82, today); } catch (ArgumentException) { }
        check(new FitnessStore(path).Goals.CalorieGoal == 42000, "Zero calorie goal is rejected without changing settings");
        var recovery = Path.Combine(fixture, "recovery-delete"); Directory.CreateDirectory(recovery);
        var old = new CalorieEntry { Date = today.AddDays(-20), Calories = 40 };
        File.WriteAllText(Path.Combine(recovery, "FitnessData.json"), JsonSerializer.Serialize(new FitnessData { Entries = new() { old } }));
        File.WriteAllText(Path.Combine(recovery, "FitnessArchive.json"), JsonSerializer.Serialize(new FitnessArchive { Entries = new() { old } }));
        var recovered = new FitnessStore(recovery); recovered.DeleteCalories(recovered.Archive.Entries.Single());
        check(!new FitnessStore(recovery).AllCalories.Any(), "Deleting a recovered archive entry cannot resurrect its stale copy");
    }
}
