# GeoQuest

A cross-platform geography trivia game inspired by the classic *Geo Challenge*, built with C#, [Avalonia UI](https://avaloniaui.net/), and the MVVM pattern.

## Game Overview

GeoQuest is a collection of fast-paced, timed mini-games in which the player must identify geographical information — flags, country outlines, cities, and landmarks. Difficulty scales up as the player answers correctly: rounds get harder, and the pool of possible answers grows.

The design goal is short, replayable sessions with immediate feedback and a visible sense of progression.

## Tech Stack

| Concern | Choice |
| --- | --- |
| Language | C# |
| UI framework | Avalonia UI 12 |
| Architecture | MVVM |
| MVVM toolkit | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4 |
| Target framework | .NET 10 |

Bindings use `AvaloniaUseCompiledBindingsByDefault`, so views should declare `x:DataType` and binding errors surface at compile time rather than at runtime.

### Platform targets

The repository currently builds a **desktop** application. Mobile — iOS in particular — is an explicit goal.

To keep that path open, all game logic must stay platform-agnostic: no `System.Windows`, no desktop-only file-system assumptions, no P/Invoke. Models, Services, and ViewModels should be portable as-is. When mobile is added, the project will be split into a shared library plus per-platform heads (`GeoQuest.Desktop`, `GeoQuest.iOS`), which requires installing the relevant workloads:

```bash
dotnet workload install ios
```

## Getting Started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run
```

To build without running:

```bash
dotnet build
```

## Project Structure

```
GeoQuest/
├── Assets/            # Images, icons and static data compiled as AvaloniaResource
│   └── Flags/         # Country flags, named by ISO 3166-1 alpha-2 code
│                      #   *.svg = sources (not shipped), *.png = generated (shipped)
├── tools/
│   └── FlagConverter/ # Dev-time SVG -> PNG rasteriser (not part of the app)
├── Models/            # Plain domain types (Country, GameSession, RoundResult, …)
├── Services/          # Data access and game logic behind interfaces
├── ViewModels/        # Presentation state and commands (CommunityToolkit.Mvvm)
├── Views/             # Avalonia XAML views and their code-behind
├── App.axaml          # Application entry point, themes and styles
├── Program.cs         # Desktop bootstrapper
└── ViewLocator.cs     # Convention-based ViewModel → View resolution
```

The layering rule: **Views** bind to **ViewModels**, which depend on **Services** through interfaces, which return **Models**. Nothing flows the other way — Models and Services never reference Avalonia types. This is what keeps the game logic unit-testable and portable to mobile.

## Assets Strategy

### Flags

Flag images live in `Assets/Flags` and are named using their **ISO 3166-1 alpha-2 code, lowercased**:

```
Assets/Flags/dk.png
Assets/Flags/us.png
Assets/Flags/mx.png
```

SVG files are the **source of truth**; the `.png` files beside them are **generated** and are what the app actually ships.

Every generated PNG is exactly **480×320**. Source flags have wildly different true proportions — Nepal is 0.83:1 and the only taller-than-wide national flag, Qatar is 2.55:1, Denmark 1.32:1 — and each is **stretched to fill** the shared canvas. This is a deliberate trade: it buys a perfectly uniform grid at the cost of showing flags at other than their official proportions. Because every image is identical in size and ratio, the view can render with `Stretch="Fill"` and every tile matches.

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

### Build action

All assets are compiled with the `AvaloniaResource` build action. This is already configured in `GeoQuest.csproj` with a wildcard, so **new files are picked up automatically** — no per-file edits needed:

```xml
<AvaloniaResource Include="Assets\**" />
```

Embedding assets as resources rather than shipping them as loose files is what makes them work identically on desktop and on mobile, where the app bundle is read-only and loose-file paths are unreliable.

### Loading assets at runtime

Assets are addressed with the `avares://` scheme:

```csharp
var uri = new Uri($"avares://GeoQuest/Assets/Flags/{isoCode}.png");
using var stream = AssetLoader.Open(uri);
var bitmap = new Bitmap(stream);
```

### Country metadata

A JSON file maps ISO codes to country metadata (display name, region, and whatever later mini-games require, such as capital city and coordinates). It ships in `Assets` as an `AvaloniaResource` alongside the images, so the flag directory and the metadata stay in sync and a missing image is a build-visible problem rather than a runtime surprise.

The ISO code is the single join key across every mini-game: it links a flag image, a country outline, and a set of cities to one country record.

## Roadmap

### Milestone 1 — "Guess the Flag" ✅

The player is given a country name and must pick the correct flag from a grid of options.

The grid is dynamic: it starts at **3** options and expands to **4**, **5**, and **6** as the player answers correctly, at 0, 3, 7 and 12 correct answers. Column counts keep each size balanced — 3 and 6 in rows of three, 4 as a square, 5 as three over two.

Rounds are timed. The clock starts at 12 seconds and drops by one second per grid size to a 7-second floor, so later rounds squeeze on both axes: more flags to scan, less time to scan them. Running out of time counts as a miss.

Scoring rewards speed and consistency — a base 100 points, up to 50% more for answering quickly, and a streak multiplier capped at 2x. A run ends after three misses.

The question pool is the **197 sovereign states**. Territories, the four `gb-*` subdivisions and the EU flag are excluded, since offering Scotland next to the United Kingdom is not a fair round.

### Milestone 2 — "Guess the Border"

Identify a country from its outline/silhouette alone. Requires country geometry (outlines rendered from vector data), plus a normalization pass so that wildly different country sizes present at a comparable, fair scale.

### Milestone 3 — "Find the City" (World Map)

Drop a pin on a blank world map as close to a target city as possible. Scoring is distance-based rather than binary, which requires a map projection and a great-circle distance calculation between the guess and the true coordinates.

### Milestone 4 — "Find the Landmark"

Pinpoint famous global monuments on the world map. Shares the pin-drop and distance-scoring mechanics from Milestone 3, with a landmark dataset in place of cities.

### Future Scope

Country-specific and sub-national modes, reusing the Milestone 3 map and scoring machinery at a tighter zoom:

- "Find the city in Denmark"
- "Find the city in the United States"
- "Find the city in Mexico"
- "Find the State/Region" — identify sub-national divisions

## Status

**Milestone 1 is playable.** Milestone 2 is not yet started.

### Country metadata

`Assets/countries.json` holds one entry per flag, keyed by ISO code:

```json
{ "code": "dk", "name": "Denmark", "region": "Europe", "kind": "sovereign" }
```

`kind` is one of `sovereign`, `territory`, `subdivision` or `other`, and is what filters the question pool. `region` is currently unused by Milestone 1 but is in place for regional filtering and the later map milestones.

The file and the flag directory must stay in sync — 255 entries, 255 images, matched by code in both directions.
