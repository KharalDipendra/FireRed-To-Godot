# ROM Asset Extractor (C#)

> **Forked from [TheJjokerR/ROM-Asset-Extractor](https://github.com/TheJjokerR/ROM-Asset-Extractor)**
> Original project by **tjtwl** — licensed under GPL-3.0.

A tool for extracting assets (sprites, maps, tilesets) from third-generation Pokémon GBA ROMs.

---

## Quick Start

### Requirements

- .NET Framework 4.7.2+
- Visual Studio 2019+ (or `dotnet build`)
- [pret/pokefirered](https://github.com/pret/pokefirered) decomp project (required for `DecompToGodot`)

### Build

1. Clone this repository.
2. Open `RomAssetExtractor.sln` in Visual Studio.
3. Restore NuGet packages and build the solution.

### Command Line

```
RomAssetExtractor.Cli.exe --rom "path/to/rom.gba"
```

**Options:**

| Flag | Description | Default |
|------|-------------|---------|
| `--rom` / `-r` | Path to the GBA ROM file **(required)** | — |
| `--output` / `-o` | Output directory for extracted assets | `output` |
| `--save-bitmaps` / `-sb` | Save bitmap images | `True` |
| `--save-trainers` / `-st` | Save trainer data | `True` |
| `--save-maps` / `-sm` | Save map data | `True` |
| `--save-map-renders` / `-smr` | Save full map renders | `False` |

### As a Library

Reference `RomAssetExtractor.csproj` in your project:

```csharp
await AssetExtractor.ExtractRom(
    "path/to/rom",
    "path/to/output",
    shouldSaveBitmaps,
    shouldSaveTrainers,
    shouldSaveMaps,
    logTextWriter
);
```

### UI

Build and run `RomAssetExtractor.UI` for a Windows Forms GUI.

![UI Showcase](screenshots/UI_Showcase.png)

![Log Showcase](screenshots/log_showcase.png)

---

## Version Support

Only English (USA) ROMs are currently tested.

| Game | Partial | Notes |
|------|---------|-------|
| Ruby | AXVE | Other language codes unsupported |
| FireRed | BPRE | Other language codes unsupported |
| LeafGreen | BPEE | Other language codes unsupported |
| Sapphire | — | Not yet supported |
| Emerald | — | Not yet supported |

Additional versions can be configured via offsets in `pokeroms.yml`.

---

## Godot Integration

![Godot Export](screenshots/godot.png)

This project includes two tools for exporting ROM map data into **Godot 4.3+** projects:

- **`RomAssetExtractor.Godot`** — Exports ROM-extracted map data as a ready-to-open Godot project with TileSet resources (`.tres`), TileMap scenes (`.tscn`), and per-map JSON files containing NPC/event data and animation metadata.

- **`DecompToGodot`** — Converts a [pret/pokefirered](https://github.com/pret/pokefirered) (or similar Gen 3) decomp project directly into Godot 4.3+ scenes, tilesets, and metadata.

  ```
  DecompToGodot.exe <decomp-path> <output-path> [map-filter]
  ```
  Example: `DecompToGodot.exe C:\pokefirered-master C:\MyGodotProject\maps PalletTown,Route1`

The output can be opened directly as a Godot project.

---

## Project Structure

| Project | Description |
|---------|-------------|
| `RomAssetExtractor` | Core library — ROM reading, asset extraction |
| `RomAssetExtractor.Cli` | Command-line interface |
| `RomAssetExtractor.UI` | Windows Forms GUI |
| `RomAssetExtractor.Godot` | Exports ROM maps as Godot 4.3+ TileMap scenes |
| `DecompToGodot` | Converts [pokefirered](https://github.com/pret/pokefirered) decomp maps to Godot scenes |

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for the full text.

**Original Copyright (C) 2021 TheJjokerR**

---

## Credits

- **Original project:** [TheJjokerR/ROM-Asset-Extractor](https://github.com/TheJjokerR/ROM-Asset-Extractor)
- Nintendo / Creatures Inc. / GAME FREAK inc. — Pokémon and character names are trademarks of Nintendo.
- Sprite extraction inspired by [magical/pokemon-gba-sprites](https://github.com/magical/pokemon-gba-sprites/)
- Offset data from [jugales/pokewebkit](https://github.com/jugales/pokewebkit)
- Bitmap/PNG writing adapted from "unLZ-GBA replacement" by Nintenlord
- Tile extraction code from [kaisermg5/jaae](https://github.com/kaisermg5/jaae)

---

## Disclaimer

This is a personal fork for educational purposes. I am not affiliated with Nintendo or Pokémon.
Nothing here constitutes legal advice. Use this software responsibly and respect all applicable
copyright laws in your jurisdiction.
