using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FitnessTracker;

internal abstract class FormWindow : Window
{
    protected readonly StackPanel Fields = new();
    internal readonly TextBlock Error = new() { Foreground = FitnessChart.Rose, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
    internal readonly Button CommitButton;
    internal readonly Button CancelButton;

    protected FormWindow(string title, string description, string action, bool destructive = false)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        NativeTheme.Apply(this);
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, Foreground = FitnessChart.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 18), LineHeight = 20 });
        panel.Children.Add(Fields);
        panel.Children.Add(Error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        CancelButton = new Button { Content = "Cancel", IsCancel = true, IsDefault = destructive, MinWidth = 90, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(16, 11, 16, 11), Style = (Style)Application.Current.FindResource("QuietButton") };
        CommitButton = new Button { Content = action, IsDefault = !destructive, MinWidth = 120 };
        if (destructive) { CommitButton.Background = FitnessChart.Rose; CommitButton.Foreground = (Brush)Application.Current.FindResource("BackgroundBrush"); }
        CommitButton.Click += (_, _) => { if (TryCommit()) DialogResult = true; };
        actions.Children.Add(CancelButton); actions.Children.Add(CommitButton);
        panel.Children.Add(actions);
        Content = panel;
        Loaded += (_, _) => { if (destructive) CancelButton.Focus(); };
    }

    protected TextBox Input(string label, string value)
    {
        var input = new TextBox { Text = value };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        Fields.Children.Add(new Label { Content = label, Target = input, Foreground = FitnessChart.Muted, Padding = new Thickness(0, 12, 0, 6) });
        Fields.Children.Add(input);
        return input;
    }
    protected static decimal Number(TextBox input, string name)
    {
        if (!FitnessStore.TryNumber(input.Text.Trim(), out var number)) throw new ArgumentException($"Enter a valid {name}.");
        return number;
    }
    protected static DateTime Date(TextBox input)
    {
        if (!DateTime.TryParseExact(input.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Use a valid date in YYYY-MM-DD format.");
        return date;
    }
    protected abstract void Commit();
    internal bool TryCommit()
    {
        try { Commit(); Error.Visibility = Visibility.Collapsed; return true; }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or OverflowException)
        {
            Error.Text = exception is ArgumentException ? exception.Message : $"Could not save. {exception.Message}";
            Error.Visibility = Visibility.Visible;
            return false;
        }
    }
}

internal sealed class EditEntryWindow : FormWindow
{
    private readonly FitnessStore store;
    private readonly CalorieEntry? calories;
    private readonly WeightEntry? weight;
    internal readonly TextBox ValueInput, DateInput;

    internal EditEntryWindow(FitnessStore store, CalorieEntry? calories, WeightEntry? weight)
        : base(weight == null ? "Edit calorie entry" : "Edit weigh-in", weight == null ? "Positive calories mean a deficit; negative calories mean a surplus." : "Update the recorded weight or date.", "Save changes")
    {
        this.store = store; this.calories = calories; this.weight = weight;
        ValueInput = Input(weight == null ? "Net calories (kcal)" : "Weight (kg)", (weight?.WeightKg ?? calories!.Calories).ToString(CultureInfo.CurrentCulture));
        DateInput = Input("Entry date · YYYY-MM-DD", (weight?.Date ?? calories!.Date).ToString("yyyy-MM-dd"));
        Loaded += (_, _) => { ValueInput.Focus(); ValueInput.SelectAll(); };
    }
    protected override void Commit()
    {
        var number = Number(ValueInput, weight == null ? "calorie amount" : "weight");
        var date = Date(DateInput);
        if (weight != null) store.UpdateWeight(weight, number, date);
        else store.UpdateCalories(calories!, number, date);
    }
}

internal sealed class DeleteEntryWindow : FormWindow
{
    private readonly FitnessStore store;
    private readonly CalorieEntry? calories;
    private readonly WeightEntry? weight;
    internal DeleteEntryWindow(FitnessStore store, CalorieEntry? calories, WeightEntry? weight, string summary)
        : base("Delete this entry?", "This removes the selected entry from your history and recalculates your charts and totals.", "Delete entry", true)
    {
        this.store = store; this.calories = calories; this.weight = weight;
        Fields.Children.Add(new Border { Background = (Brush)Application.Current.FindResource("CardBrush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(16), Child = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, LineHeight = 24, FontWeight = FontWeights.SemiBold } });
    }
    protected override void Commit()
    {
        if (weight != null) store.DeleteWeight(weight); else store.DeleteCalories(calories!);
    }
}

internal sealed class EditGoalsWindow : FormWindow
{
    // Which goal field the user is typing in. Typing in one locks the other; clearing it unlocks both.
    internal enum GoalSource { Unchanged, Calories, Weight }

    private readonly FitnessStore store;
    private readonly WeightEntry? latestLow;
    private bool updating;
    internal readonly TextBox CalorieGoalInput, WeightGoalInput, TargetDateInput;
    internal readonly TextBlock GoalHint = new() { Foreground = FitnessChart.Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 10, 0, 0) };
    internal GoalSource Source { get; private set; }

    internal EditGoalsWindow(FitnessStore store)
        : base("Edit your goals", "Enter a calorie goal or a target weight, not both. A target weight sets the calorie goal from your latest L weigh-in.", "Save goals")
    {
        this.store = store;
        latestLow = store.LatestWeight(DateTime.Today);
        CalorieGoalInput = Input("Calorie goal (kcal)", Format(store.Goals.CalorieGoal));
        WeightGoalInput = Input("Target weight (kg)", Format(store.Goals.TargetWeightKg));
        Fields.Children.Add(GoalHint);
        TargetDateInput = Input("Target date · YYYY-MM-DD", store.Goals.TargetDate.ToString("yyyy-MM-dd"));
        CalorieGoalInput.TextChanged += (_, _) => { if (!updating) SetSource(CalorieGoalInput.Text.Trim().Length > 0 ? GoalSource.Calories : GoalSource.Unchanged); };
        WeightGoalInput.TextChanged += (_, _) => { if (!updating) SetSource(WeightGoalInput.Text.Trim().Length > 0 ? GoalSource.Weight : GoalSource.Unchanged); };
        SetSource(GoalSource.Unchanged);
        Loaded += (_, _) => { CalorieGoalInput.Focus(); CalorieGoalInput.SelectAll(); };
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private void SetSource(GoalSource source)
    {
        Source = source;
        updating = true;
        try
        {
            CalorieGoalInput.IsEnabled = source != GoalSource.Weight;
            WeightGoalInput.IsEnabled = source != GoalSource.Calories;
            switch (source)
            {
                case GoalSource.Calories:
                    WeightGoalInput.Text = Format(store.Goals.TargetWeightKg);
                    GoalHint.Text = $"Using your calorie goal; the target weight stays at {Format(store.Goals.TargetWeightKg)} kg. Clear the calorie goal to enter a target weight instead.";
                    break;
                case GoalSource.Weight:
                    var valid = TryCalculate(out _, out var calories, out var message);
                    CalorieGoalInput.Text = valid ? Format(calories) : "";
                    GoalHint.Text = valid ? message + " Clear the target weight to enter calories instead." : message;
                    break;
                default:
                    // A field was cleared (or nothing typed yet): restore the other one to the saved goal.
                    if (CalorieGoalInput.Text.Trim().Length == 0) WeightGoalInput.Text = Format(store.Goals.TargetWeightKg);
                    if (WeightGoalInput.Text.Trim().Length == 0) CalorieGoalInput.Text = Format(store.Goals.CalorieGoal);
                    GoalHint.Text = "Type in one field and the other locks. Clear it to switch.";
                    break;
            }
        }
        finally { updating = false; }
    }

    // Target weight → calories to lose from the latest L weigh-in at 7,700 kcal/kg.
    private bool TryCalculate(out decimal target, out decimal calories, out string message)
    {
        calories = 0;
        if (!FitnessStore.TryNumber(WeightGoalInput.Text.Trim(), out target) || target <= 0)
        {
            message = "Enter a valid target weight in kg.";
            return false;
        }
        if (latestLow == null)
        {
            message = "Record an L weigh-in first. The calorie goal is calculated from your latest L weight.";
            return false;
        }
        calories = FitnessStore.CaloriesToReach(latestLow.WeightKg, target);
        if (calories <= 0)
        {
            message = $"Target weight must be below your latest L weigh-in ({Format(latestLow.WeightKg)} kg on {latestLow.Date:MMM d}).";
            return false;
        }
        message = $"{Format(latestLow.WeightKg)} kg (L, {latestLow.Date:MMM d}) − {Format(target)} kg = {Format(latestLow.WeightKg - target)} kg × 7,700 = {calories:N0} kcal to lose.";
        return true;
    }

    protected override void Commit()
    {
        var date = Date(TargetDateInput);
        switch (Source)
        {
            case GoalSource.Calories:
                store.UpdateGoals(Number(CalorieGoalInput, "calorie goal"), store.Goals.TargetWeightKg, date);
                break;
            case GoalSource.Weight:
                if (!TryCalculate(out var target, out var calories, out var message)) throw new ArgumentException(message);
                store.UpdateGoals(calories, target, date);
                break;
            default:
                store.UpdateGoals(store.Goals.CalorieGoal, store.Goals.TargetWeightKg, date);
                break;
        }
    }
}
