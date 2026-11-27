# Fitness Tracker: agent navigation

## Start here

- Native C# / .NET 8 WPF desktop app; no web frontend, database, MVVM framework, or chart packages.
- Project root is the folder containing `Fitness Tracker.csproj`. If your cwd is `bin/Debug/net8.0`, go up three directories for sources. Solution: `../Fitness Tracker.sln`.
- Read only the files in the task table below first. Exclude `bin`, `obj`, `.build-profile`, `.packages`, and `.git` from broad searches. Use `rg -n 'symbol' File.cs` to jump to relevant code.
- Build: `build.cmd`. Regression checks + rendered PNGs: `build.cmd check`. Close the running app before building. Do not clean the output directory: it contains personal history.

## Task → source

| Task | Files / entry points |
| --- | --- |
| Dashboard layout, weekly cards, entry forms, history rows | `MainWindow.xaml` (named controls, Click handlers) |
| Dashboard calculations, saves, paging, child windows | `MainWindow.xaml.cs`: `Refresh`, `SaveCalories`, `SaveWeight`, `ShowHistoryPage`, `OpenChart`, `OpenWeeklyCalories` |
| Weekly history aggregation | `WeeklyCalorieData.cs`: `Create`, `WeeklyCalorieSummary` |
| Weekly history list, paging, chart selection | `WeeklyCaloriesWindow.xaml` and `.xaml.cs`: `Refresh`, `ShowPage`, `WeeklyRow` (12 weeks/page) |
| All charts and chart pop-outs | `FitnessChart.cs`: `ChartKind`, `Redraw`, `DrawWeight`, `DrawCalories`, `DrawProgress`, `DrawWeeklyCalories`, `Axes`, `ChartWindow` |
| Daily L readings and weekly weight averages | `WeightChartData.cs`: `Create`; `WeightEntry.cs`: weight JSON models |
| Load, save, archive, edit/delete entries, goals | `FitnessStore.cs`: models, `AllCalories`, `DailyTotals`, `LatestWeight`, `Add*`, `Update*`, `Delete*`, `Save` |
| Entry edit/delete dialogs and goal editor | `EditDialogs.cs`: `EditEntryWindow`, `DeleteEntryWindow`, `EditGoalsWindow` |
| Theme resources, control templates | `Theme.xaml`; `Palettes.cs`: `BuiltInPalettes`, `PaletteStore`, `ThemeManager` |
| Native title bar appearance | `MainWindow.xaml.cs`: `NativeTheme` |
| Startup, single-instance mutex, load failures | `App.xaml.cs`; `App.xaml` merges theme resources |
| Regression tests and screenshot rendering | `Checks/Program.cs` (STA WPF harness), `Checks/MutationChecks.cs`, `Checks/WeeklyChecks.cs` |
| SDK, build, packaging | `global.json`, `Fitness Tracker.csproj`, `build.cmd`, `NuGet.Config`, `Properties/PublishProfiles/Portable.pubxml` |
| User-facing behavior and operations | `README.md`; icons: `Assets/ICON.md`, `Assets/FitnessTracker.ico` |

## Data and update flow

`App.OnStartup` → `FitnessStore(AppContext.BaseDirectory)` → `MainWindow(store)`.
UI handlers validate and call store mutations → atomic save → `FitnessStore.Changed` → `MainWindow.Refresh` → dashboard + all open charts + weekly history. `ThemeManager.Changed` and the dashboard's minute timer also refresh views (timer only on date change). Owned nonmodal windows are reused, restore from minimized, and close with the dashboard; Escape closes a pop-out.

Persistent files live **beside the executable**, normally `bin/Debug/net8.0`:

| File | Purpose |
| --- | --- |
| `FitnessData.json` | Recent calorie entries plus undated `LegacyCalories` |
| `FitnessArchive.json` | Older calorie entries and tracking start date |
| `WeightHistory.json` | L weigh-ins and historical H records |
| `FitnessGoals.json` | Calorie goal, target weight/date |
| `FitnessSettings.txt` | Optional legacy balance imported only if no FitnessData JSON |
| `ColorPalettes.json` | Palettes and selected theme |

Do not overwrite personal JSON or `.bak` files, delete `bin`, or run `dotnet clean` without preserving history. Tests use isolated fixtures/copies in `bin/Debug/net8.0/Verification`; render output lives there too. Store saves use `.tmp` + atomic replacement + `.bak`; loading reconciles interrupted archive writes in memory. Calorie saves archive entries older than 14 days; edits stay in their owning file. Always report through `AllCalories`/`DailyTotals`, not just the active file. Preserve distinct identical entries and optional IDs during edits/recovery.

## Calculation conventions

- Positive kcal = deficit, negative = surplus. Entry dates are date-only and cannot be future dates.
- Weeks start Monday (`FitnessStore.WeekStart`). Past weekly averages divide by 7; current averages divide by elapsed calendar days including today. Logged days count distinct dates, including zero balances. Missing weeks are displayed, not silently omitted.
- Weekly history starts at the earliest dated calorie entry and excludes undated `LegacyCalories`; cumulative progress includes that balance. Weekly chart is chronological for the displayed page; list is newest first.
- Estimated weight uses 7,700 kcal/kg. `EstimatedWeight`/`Kilograms` centralize formatting (grams below 1 kg).
- Weight goal calculations use latest **L**, not H; last same-day entry wins. The chart plots the last L reading per day and its Monday–Sunday weekly average. History shows L weigh-ins. Stored H records are preserved on save.
- Use dynamic brush resources and refresh custom Canvas charts on palette changes. No extra chart dependencies needed.

## Verification shortcuts

`build.cmd check` builds both projects and runs the WPF harness. `Properties/AssemblyInfo.cs` exposes internals to `Checks`. UI checks pump the dispatcher, operate on fixtures, and render windows with `RenderTargetBitmap`. Inspect relevant PNGs after layout changes, including minimum-size windows. Weekly coverage includes year/week boundaries, partial/empty weeks, archives, missing vs zero, paging, live mutations, palette refresh, and window lifecycle. Build output: `bin/Debug/net8.0/Fitness Tracker.exe` (desktop shortcut target).

Keep this map updated when files, entry points, or data conventions change.
