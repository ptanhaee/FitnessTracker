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
    public const string Moonlight = "Moonlight";
    public const string MoonlightMidnight = "Moonlight Midnight";

    // Earlier names, renamed in saved settings on load.
    public static readonly Dictionary<string, string> Renamed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space Within"] = Moonlight,
        ["Space Within Midnight"] = MoonlightMidnight,
    };
    public const string Original = "Original";

    // Key order: Original | Moonlight (dark gray) | Moonlight Midnight. Colors come from the "Space Within" palette.
    private static readonly (string Key, string Original, string Gray, string Midnight)[] Table =
    {
        ("BackgroundBrush",    "#0C111B", "#18181C", "#080719"),
        ("CardBrush",          "#151D2B", "#222228", "#100E32"),
        ("InputBrush",         "#0E1522", "#1A1A1F", "#080719"),
        ("BorderBrush",        "#293449", "#34343E", "#1F1B64"),
        ("TextBrush",          "#EDF3FC", "#EDECF8", "#E7E6F8"),
        ("MutedBrush",         "#9AAAC1", "#A9A8C4", "#B7B4EA"),
        ("AccentBrush",        "#69E0C2", "#4CC9F0", "#4CC9F0"),
        ("NegativeBrush",      "#FF8B9D", "#F84294", "#F84294"),
        ("AccentTextBrush",    "#0B2823", "#031D25", "#031D25"),
        ("MenuBrush",          "#1E2A3B", "#2A2A32", "#17154B"),
        ("MenuHoverBrush",     "#34455D", "#3A3A46", "#27227D"),
        ("QuietButtonBrush",   "#243247", "#2E2E38", "#1F1B64"),
        ("SelectionBrush",     "#367B72", "#3F37C9", "#3F37C9"),
        ("CheckedBrush",       "#21453F", "#17154B", "#1F1B64"),
        ("ToolTipBrush",       "#253349", "#2A2A32", "#17154B"),
        ("ScrollThumbBrush",   "#3B4B63", "#45454F", "#27227D"),
        ("ProgressTrackBrush", "#2A3649", "#34343E", "#1F1B64"),
        ("RowSelectedBrush",   "#253349", "#2E2E38", "#1F1B64"),
        ("RowHoverBrush",      "#1E2A3B", "#28282F", "#17154B"),
        ("RowDividerBrush",    "#263146", "#2E2E36", "#17154B"),
        ("GoalPanelBrush",     "#213A37", "#17154B", "#1F1B64"),
        ("ChartFillBrush",     "#193D39", "#063949", "#063949"),
        ("GridBrush",          "#293449", "#303039", "#1F1B64"),
        ("HighBrush",          "#FFB975", "#E956D3", "#E956D3"),
        ("LowBrush",           "#80B7FF", "#4895EF", "#4895EF"),
        ("MidpointBrush",      "#69E0C2", "#4CC9F0", "#4CC9F0"),
    };

    public static IEnumerable<string> Keys => Table.Select(t => t.Key);

    public static List<ColorPalette> All() => new()
    {
        new ColorPalette { Name = Moonlight, Colors = Table.ToDictionary(t => t.Key, t => t.Gray) },
        new ColorPalette { Name = MoonlightMidnight, Colors = Table.ToDictionary(t => t.Key, t => t.Midnight) },
        new ColorPalette { Name = Original, Colors = Table.ToDictionary(t => t.Key, t => t.Original) },
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
        // Carry renamed built-ins (and the selection) over to their new names instead of duplicating them.
        foreach (var palette in settings.Palettes)
        {
            if (!BuiltInPalettes.Renamed.TryGetValue(palette.Name, out var newName) || settings.Palettes.Any(p => Same(p.Name, newName))) continue;
            if (Same(settings.Selected, palette.Name)) settings.Selected = newName;
            palette.Name = newName;
            changed = true;
        }
        var builtIns = BuiltInPalettes.All();
        foreach (var builtIn in builtIns.Where(b => !settings.Palettes.Any(p => Same(p.Name, b.Name))))
        {
            settings.Palettes.Add(builtIn);
            changed = true;
        }
        // Fill colors missing from hand-edited palettes (built-in values first, otherwise Moonlight).
        foreach (var palette in settings.Palettes)
        {
            palette.Colors ??= new();
            var fallback = builtIns.FirstOrDefault(b => Same(b.Name, palette.Name)) ?? builtIns[0];
            foreach (var key in BuiltInPalettes.Keys.Where(k => !palette.Colors.ContainsKey(k)))
                palette.Colors[key] = fallback.Colors[key];
        }
        if (!settings.Palettes.Any(p => Same(p.Name, settings.Selected)))
        {
            settings.Selected = BuiltInPalettes.Moonlight;
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
