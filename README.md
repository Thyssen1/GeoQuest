# GeoQuest

**A fast-paced geography trivia game for desktop, built with C# and Avalonia UI.**

Inspired by the classic *Geo Challenge*, GeoQuest is a set of timed mini-games that ask you to identify places from flags, outlines, cities and landmarks. Answer correctly and it gets harder — more options to choose from, less time to choose.

![GeoQuest — the Guess the Flag mini-game](docs/screenshot.png)

[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download)
[![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)](https://avaloniaui.net/)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Status](https://img.shields.io/badge/milestone%201-playable-2ECC71)

## How it plays

You are shown a country name and a grid of flags, and you pick the right one before the clock runs out.

- **The grid grows as you improve.** It opens at 3 flags and expands to 4, 5 and 6 at 3, 7 and 12 correct answers.
- **The clock tightens too.** Rounds start at 12 seconds and lose a second per grid size, down to a 7-second floor — so later rounds squeeze on both axes at once.
- **Speed and streaks pay.** A correct answer is worth 100 points, up to 50% more for answering fast, multiplied by a streak bonus that caps at 2×.
- **Three misses ends the run.** A wrong pick or a timeout costs a life and resets your streak.
- **Your best score persists** between sessions, stored under your user application data directory.

Answer with the mouse, or press **1–6** on the number row or numpad.

Questions are drawn from the **197 sovereign states**. Territories, the four UK home nations and the EU flag ship with the app but are kept out of the question pool — offering Scotland alongside the United Kingdom would not be a fair round.

## Status

| Milestone | State |
| --- | --- |
| **1 — Guess the Flag** | ✅ Playable |
| 2 — Guess the Border | Not started |
| 3 — Find the City | Not started |
| 4 — Find the Landmark | Not started |

## Getting Started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). No other setup — the flag assets are committed, so a fresh clone runs as-is.

```bash
git clone https://github.com/Thyssen1/GeoQuest.git
cd GeoQuest
dotnet run
```

Run the tests:

```bash
dotnet test
```

### Build configurations

`GeoQuest.sln` defines **Debug|Any CPU**, **Debug|x64**, **Release|Any CPU** and **Release|x64**. The x64 configurations produce genuinely 64-bit assemblies (`Amd64` rather than `MSIL`) in `bin/x64/`, leaving the AnyCPU output in `bin/` untouched.

```bash
dotnet build GeoQuest.sln -c Release -p:Platform=x64
```

The classic `.sln` format is used deliberately over the newer `.slnx`, which requires Visual Studio 2022 17.14+ or Rider 2025.

## Tech Stack

| Concern | Choice |
| --- | --- |
| Language | C# |
| UI framework | Avalonia UI 12 |
| Architecture | MVVM |
| MVVM toolkit | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4 |
| Target framework | .NET 10 |

Bindings use `AvaloniaUseCompiledBindingsByDefault`, so views declare `x:DataType` and binding errors surface at compile time rather than at runtime.

### Platform targets

The repository currently builds a **desktop** application. Mobile — iOS in particular — is an explicit goal.

To keep that path open, all game logic stays platform-agnostic: no `System.Windows`, no desktop-only file-system assumptions, no P/Invoke. Models, Services and ViewModels are portable as-is. When mobile is added, the project will split into a shared library plus per-platform heads (`GeoQuest.Desktop`, `GeoQuest.iOS`), which requires the relevant workload:

```bash
dotnet workload install ios
```

## Project Structure

```
GeoQuest/
├── Assets/            # Images and static data compiled as AvaloniaResource
│   ├── countries.json # ISO code -> name, region, kind
│   └── Flags/         # Country flags, named by ISO 3166-1 alpha-2 code
│                      #   *.svg = sources (not shipped), *.png = generated (shipped)
├── Models/            # Plain domain types (Country, FlagQuestion, GameRules)
├── Services/          # Data access and question generation behind interfaces
├── ViewModels/        # Presentation state and commands (CommunityToolkit.Mvvm)
├── Views/             # Avalonia XAML views and their code-behind
├── tools/
│   └── FlagConverter/ # Dev-time SVG -> PNG rasteriser (not part of the app)
├── tests/
│   └── GeoQuest.Tests/ # xUnit suite over rules, data and question generation
├── App.axaml          # Application entry point, themes and styles
├── Program.cs         # Desktop bootstrapper
└── ViewLocator.cs     # Convention-based ViewModel → View resolution
```

The layering rule: **Views** bind to **ViewModels**, which depend on **Services** through interfaces, which return **Models**. Nothing flows the other way — Models and Services never reference Avalonia types. This is what keeps the game logic unit-testable and portable to mobile.

Two small adapters are the deliberate exception: `FlagImageLoader` and `AssetCountryData` know about `avares://`, so nothing else has to.

`tools/` and `tests/` are removed from the root project's compile globs — the app project lives at the repository root, so its default `**/*.cs` would otherwise compile both into the game.

## Assets Strategy

### Flags

Flag images live in `Assets/Flags`, named by their **ISO 3166-1 alpha-2 code, lowercased**:

```
Assets/Flags/dk.png
Assets/Flags/us.png
Assets/Flags/mx.png
```

SVG files are the **source of truth**; the `.png` files beside them are **generated** and are what the app ships.

Every generated PNG is exactly **480×320**. Source flags have wildly different true proportions — Nepal is 0.83:1 and the only taller-than-wide national flag, Qatar is 2.55:1, Denmark 1.32:1 — and each is **stretched to fill** the shared canvas. This is a deliberate trade: a perfectly uniform grid, at the cost of showing flags at other than their official proportions. Because every image is identical in size and ratio, the view renders with `Stretch="Fill"` and every tile matches.

### Regenerating the PNGs

The game loads PNGs through Avalonia's built-in `Bitmap`, which keeps the app free of any SVG rendering dependency. Conversion happens at dev time via a committed tool:

```bash
dotnet run --project tools/FlagConverter -- --height 320 --fill --force
```

Existing PNGs newer than their source are skipped, so `--force` is needed when changing output mode. Three modes are available:

| Flag | Output |
| --- | --- |
| `--fill` | Stretch to fill the shared canvas. Uniform size, distorted proportions. **Current setting.** |
| *(default)* | Centre on the shared canvas with transparent padding. Uniform size, true proportions, visible padding. |
| `--native` | Pin height only, width follows the source `viewBox`. True proportions, ragged grid. |

`--ratio` sets the canvas shape (default `1.5`, i.e. 3:2). Re-run after adding or updating any flag.

Switching mode requires a matching change to the `Image` in [Views/GameView.axaml](Views/GameView.axaml): `Stretch="Fill"` suits `--fill`, while the other two modes want `Stretch="Uniform"`.

The `tools/` directory is excluded from the main project's compile globs, and `Assets/Flags/*.svg` is excluded from `AvaloniaResource`, so the SVG sources stay in the repo without being embedded in the shipped app.

### Country metadata

`Assets/countries.json` holds one entry per flag, keyed by ISO code:

```json
{ "code": "dk", "name": "Denmark", "region": "Europe", "kind": "sovereign" }
```

`kind` is one of `sovereign`, `territory`, `subdivision` or `other`, and is what filters the question pool. `region` is unused by Milestone 1 but is in place for regional filtering and the later map milestones.

The file and the flag directory must stay in sync — **255 entries, 255 images**, matched by code in both directions. The ISO code is the single join key across every mini-game: it links a flag image, a country outline and a set of cities to one country record.

### Build action

All assets are compiled with the `AvaloniaResource` build action, configured in `GeoQuest.csproj` with a wildcard, so **new files are picked up automatically** — no per-file edits needed:

```xml
<AvaloniaResource Include="Assets\**" Exclude="Assets\Flags\*.svg" />
```

Embedding assets as resources rather than shipping loose files is what makes them work identically on desktop and on mobile, where the app bundle is read-only and loose-file paths are unreliable. Assets are addressed with the `avares://` scheme:

```csharp
var uri = new Uri($"avares://GeoQuest/Assets/Flags/{isoCode}.png");
using var stream = AssetLoader.Open(uri);
var bitmap = new Bitmap(stream);
```

## Roadmap

### Milestone 1 — "Guess the Flag" ✅

Complete. See [How it plays](#how-it-plays) for the rules as implemented.

### Milestone 2 — "Guess the Border"

Identify a country from its outline/silhouette alone. Requires country geometry rendered from vector data, plus a normalisation pass so wildly different country sizes present at a comparable, fair scale.

### Milestone 3 — "Find the City" (World Map)

Drop a pin on a blank world map as close to a target city as possible. Scoring is distance-based rather than binary, which requires a map projection and a great-circle distance calculation between the guess and the true coordinates.

### Milestone 4 — "Find the Landmark"

Pinpoint famous global monuments on the world map. Shares the pin-drop and distance-scoring mechanics from Milestone 3, with a landmark dataset in place of cities.

### Future scope

Country-specific and sub-national modes, reusing the Milestone 3 map and scoring machinery at a tighter zoom:

- "Find the city in Denmark"
- "Find the city in the United States"
- "Find the city in Mexico"
- "Find the State/Region" — identify sub-national divisions

## Credits

Flag artwork comes from [hampusborgos/country-flags](https://github.com/hampusborgos/country-flags), a set of accurate flag renders sourced from Wikimedia Commons and checked against the relevant national legislation. That project states the flags are **in the public domain**, on the basis that flags are not subject to copyright protection — while noting that individual countries may impose separate, non-copyright restrictions on how their flag is used.

Only the SVG sources are taken from upstream. The `.png` files in this repository are generated from them by `tools/FlagConverter` and are not upstream artifacts. Upstream also ships pre-rendered PNGs at 100/250/1000px widths; GeoQuest does not use them, because it needs a uniform 480×320 canvas rather than native proportions.

The naming convention here follows upstream directly: ISO 3166-1 alpha-2, plus the six-character `gb-eng`/`gb-sct`/`gb-wls`/`gb-nir` codes for the UK's constituent countries, and the user-assigned `xk` for Kosovo.

Country names, regions and `kind` classifications in `Assets/countries.json` were compiled for this project and are not from upstream.
