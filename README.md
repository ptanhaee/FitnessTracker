# Fitness Tracker desktop

Launch `bin/Debug/net8.0/Fitness Tracker.exe` (the desktop shortcut points there). This is a native WPF Windows application with no console window and no third-party chart libraries.

The dashboard contains weekly comparisons, net deficit, weight goal estimates, full entry history, calorie and weight entry forms, and three charts: H/L weight trends with interpolated midpoint, the last 14 days of calorie balances, and cumulative progress. Click a chart or its **Expand** button to open a resizable, nonmodal window. Enter/Space also expands a focused chart. Expanded charts update immediately on save; Escape closes them. Opening a chart again activates its existing window.

Positive calories mean a deficit; negative calories mean a surplus. Entry dates use `YYYY-MM-DD` and cannot be in the future. Weights must be positive, with L selected by default. The latest same-day measurement wins, and each weight chart series uses the last measurement of its type for each day. Weight-change and target-deficit calculations retain the original 7,700 kcal/kg estimate. Estimated weight changes under 1 kg are shown in grams (e.g. 450 g).

History shows **15 entries per page**, newest first, with Previous/Next controls. Right-click any entry to **Edit** its amount/date (and H/L for weights) or **Delete** it. Deletion asks for confirmation; Cancel leaves the record untouched. Identical entries are handled individually. Saving refreshes summaries and all open charts immediately. Removing the last entry on a page moves back to the preceding page.

Click **Edit** on the weight-goal card or **Edit goals** under cumulative progress to change the calorie goal, target weight, and target date. Enter either a calorie goal or a target weight, not both: typing in one field locks the other, and clearing it unlocks both. A calorie goal leaves the target weight unchanged. A target weight sets the calorie goal to the calories you need to lose from your latest **L** weigh-in: (latest L − target) × 7,700 kcal, e.g. 94 kg → 80 kg = 107,800 kcal. The target must be below that L weight, and an L weigh-in is required. Defaults remain 38,500 kcal and 80 kg by May 1, 2027 until changed. Goals must be positive numbers. Target dates can be any valid date; if the target is not after the latest weigh-in, a daily deficit cannot be calculated. Settings persist in `FitnessGoals.json` beside the executable.

## Color palettes

Pick a palette with the chips at the bottom of the dashboard; the whole app (charts, dialogs, chart windows, title bar) recolors immediately and the choice is remembered.

- **Moonlight** (default): dark gray with cyan, pink, magenta and blue from the Space Within color palette.
- **Moonlight Midnight**: deep indigo built from Space Within palette shades.
- **Original**: the previous navy and mint colors.

Palettes live in `ColorPalettes.json` beside the executable. To add one, copy a palette entry, give it a new `Name` and change its `#RRGGBB` values; missing or invalid colors fall back to the defaults, and a file that cannot be read is never overwritten.

## Data

The existing `FitnessData.json`, `FitnessArchive.json`, `WeightHistory.json`, and optional `FitnessSettings.txt` remain beside the executable, along with `FitnessGoals.json` after the first goal edit. Loading the app does not rewrite them. Saving uses temporary files and atomic replacements, with the previous version stored as `.bak`. Calorie entries older than the latest 14 days are archived on calorie entry saves. Edits remain in their original file to make each edit atomic; reports include both files regardless of entry date. New and edited calorie records include an optional identity field to preserve distinct identical entries during archive recovery. Interrupted archive writes are reconciled on startup. Invalid JSON stops startup without overwriting history. A second desktop instance is blocked to avoid concurrent desktop writes.

## Build and check

Close the running app before rebuilding. From this directory, run:

```text
build.cmd
build.cmd check
```

The build uses the installed .NET 8 SDK, keeps build caches here, and uses the local offline NuGet configuration (there are no package dependencies). The output goes to `bin/Debug/net8.0`, beside the existing history. Checks use isolated fixtures and copies of real data; they do not modify the original history. Rendered screenshots and results go in `bin/Debug/net8.0/Verification`.

Building the solution in Rider, or `dotnet build "Fitness Tracker.csproj"`, also works. A portable Windows x64 build can be produced with `dotnet publish "Fitness Tracker.csproj" -p:PublishProfile=Portable --source https://api.nuget.org/v3/index.json`; this requires downloading the runtime and writes to `bin/Portable`. Copy existing JSON history separately into the portable output folder.

## Source location

This folder is the only Fitness Tracker project; the old console version has been removed. `Fitness Tracker.sln` one folder up opens it in Rider. Don't run `dotnet clean` or delete `bin/Debug/net8.0` without copying the history files out first; they live beside the executable.
