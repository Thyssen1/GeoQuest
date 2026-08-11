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

Either SVG or high-resolution PNG is acceptable. PNGs should be large enough to render crisply on high-DPI displays and in the largest grid cell size used by the "Guess the Flag" grid.

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

### Milestone 1 — "Guess the Flag"

The player is given a country name and must pick the correct flag from a grid of options.

The grid is dynamic: it starts at **3** options and expands to **4**, **5**, and **6** as the player answers correctly. Layout must reflow gracefully at each size rather than assuming a fixed row/column count.

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

Milestone 1 is not yet started. This commit establishes the project skeleton, folder structure, and roadmap only.
