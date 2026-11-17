using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace FitnessTracker;

/// <summary>A named color scheme. Keys are the brush resource names used by Theme.xaml and the charts.</summary>
internal sealed class ColorPalette
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = new();
}

internal sealed class PaletteSettings
{
    public string? Selected { get; set; }
    public List<ColorPalette> Palettes { get; set; } = new();
}

internal static class BuiltInPalettes
{
    public const string Original = "Original";
    public const string Light = "Light";
    public static readonly HashSet<string> Retired = new(StringComparer.OrdinalIgnoreCase)
        { "Moonlight", "Moonlight Midnight", "Space Within", "Space Within Midnight" };

    // Original navy and mint, paired with a warm ivory and teal light palette.
    private static readonly (string Key, string Original, string Light)[] Table =
    {
        ("BackgroundBrush",    "#0C111B", "#F5F4EF"),
        ("CardBrush",          "#151D2B", "#FFFFFF"),
        ("InputBrush",         "#0E1522", "#F8FAF8"),
        ("BorderBrush",        "#293449", "#D5DFDA"),
        ("TextBrush",          "#EDF3FC", "#243D37"),
        ("MutedBrush",         "#9AAAC1", "#5D7069"),
        ("AccentBrush",        "#69E0C2", "#147565"),
        ("NegativeBrush",      "#FF8B9D", "#B44860"),
        ("AccentTextBrush",    "#0B2823", "#FFFFFF"),
        ("MenuBrush",          "#1E2A3B", "#FFFFFF"),
        ("MenuHoverBrush",     "#34455D", "#E8F1EC"),
        ("QuietButtonBrush",   "#243247", "#EAF0EB"),
        ("SelectionBrush",     "#367B72", "#A8D7C8"),
        ("CheckedBrush",       "#21453F", "#DFEFE6"),
        ("ToolTipBrush",       "#253349", "#FFFFFF"),
        ("ScrollThumbBrush",   "#3B4B63", "#A6B9AE"),
        ("ProgressTrackBrush", "#2A3649", "#DEE9E2"),
        ("RowSelectedBrush",   "#253349", "#DFEFE6"),
        ("RowHoverBrush",      "#1E2A3B", "#F0F6F2"),
        ("RowDividerBrush",    "#263146", "#E3EAE5"),
        ("GoalPanelBrush",     "#213A37", "#EDF5EE"),
        ("ChartFillBrush",     "#193D39", "#DCEEE5"),
        ("GridBrush",          "#293449", "#E1E8E3"),
        ("HighBrush",          "#FFB975", "#AB622B"),
        ("LowBrush",           "#80B7FF", "#366FB4"),
        ("MidpointBrush",      "#69E0C2", "#147565"),
    };

    public static IEnumerable<string> Keys => Table.Select(t => t.Key);

    public static List<ColorPalette> All() => new()
    {
        new ColorPalette { Name = Original, Colors = Table.ToDictionary(t => t.Key, t => t.Original) },
        new ColorPalette { Name = Light, Colors = Table.ToDictionary(t => t.Key, t => t.Light) },
    };
}

/// <summary>Palettes and the selected palette, stored in ColorPalettes.json beside the executable.</summary>
internal sealed class PaletteStore
{
    public const string FileName = "ColorPalettes.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;
    private readonly PaletteSettings settings;
    private readonly bool canSave;

    private PaletteStore(string path, PaletteSettings settings, bool canSave) { this.path = path; this.settings = settings; this.canSave = canSave; }

    public IReadOnlyList<ColorPalette> Palettes => settings.Palettes;
    public ColorPalette Current => settings.Palettes.FirstOrDefault(p => Same(p.Name, settings.Selected)) ?? settings.Palettes[0];

    public static PaletteStore Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        PaletteSettings? settings = null;
        var canSave = true;
        if (File.Exists(path))
        {
            try { settings = JsonSerializer.Deserialize<PaletteSettings>(File.ReadAllText(path), Json); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { canSave = false; } // never overwrite a file being hand-edited
        }
        settings ??= new PaletteSettings();
        settings.Palettes = (settings.Palettes ?? new()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();

        var changed = !File.Exists(path);
        // Remove retired built-ins from existing installations as well as fresh ones.
        changed |= settings.Palettes.RemoveAll(p => BuiltInPalettes.Retired.Contains(p.Name)) > 0;
        var builtIns = BuiltInPalettes.All();
        foreach (var builtIn in builtIns.Where(b => !settings.Palettes.Any(p => Same(p.Name, b.Name))))
        {
            settings.Palettes.Add(builtIn);
            changed = true;
        }
        // Fill colors missing from hand-edited palettes (built-in values first, otherwise Original).
        foreach (var palette in settings.Palettes)
        {
            palette.Colors ??= new();
            var fallback = builtIns.FirstOrDefault(b => Same(b.Name, palette.Name)) ?? builtIns[0];
            foreach (var key in BuiltInPalettes.Keys.Where(k => !palette.Colors.ContainsKey(k)))
                palette.Colors[key] = fallback.Colors[key];
        }
        if (!settings.Palettes.Any(p => Same(p.Name, settings.Selected)))
        {
            settings.Selected = BuiltInPalettes.Original;
            changed = true;
        }
        var store = new PaletteStore(path, settings, canSave);
        if (changed) store.Save();
        return store;
    }

    public void Select(string name)
    {
        if (!settings.Palettes.Any(p => Same(p.Name, name))) return;
        settings.Selected = name;
        Save();
    }

    private void Save()
    {
        if (!canSave) return;
        try
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // a read-only folder must not stop the app
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Applies a palette by replacing application brush resources; DynamicResource references update live.</summary>
internal static class ThemeManager
{
    private static PaletteStore? store;
    internal static event Action? Changed;

    internal static PaletteStore Store
    {
        get => store ??= PaletteStore.Load(AppContext.BaseDirectory);
        set { store = value; Apply(value.Current); }
    }

    internal static void Initialize() => Apply(Store.Current);

    internal static void Select(ColorPalette palette)
    {
        if (ReferenceEquals(palette, Store.Current)) return;
        Store.Select(palette.Name);
        Apply(Store.Current);
        Changed?.Invoke();
    }

    internal static void Apply(ColorPalette palette)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;
        var fallback = BuiltInPalettes.All()[0].Colors;
        foreach (var key in BuiltInPalettes.Keys)
            resources[key] = ToBrush(palette.Colors.GetValueOrDefault(key), fallback[key]);
    }

    internal static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? ToBrush(BuiltInPalettes.All()[0].Colors[key], "#FFFFFF");

    internal static SolidColorBrush ToBrush(string? hex, string fallback)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(hex ?? fallback); }
        catch (Exception e) when (e is FormatException or NotSupportedException) { color = (Color)ColorConverter.ConvertFromString(fallback); }
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
