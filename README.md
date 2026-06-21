# Satisfactory Node Editor

Satisfactory Node Editor is a Windows desktop tool for editing Satisfactory resource-node assignments in compatible `.sav` files. It reads a save, visualizes resource nodes (including wells and geysers) on an interactive map, lets the user preview resource and purity changes, and writes a new save file without overwriting the original.

This README documents both the current product behavior and the architecture developers need to understand before changing it.

Always back up Satisfactory saves before loading generated files in the game.

See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Current Status

The application is a WPF desktop app backed by a Node.js save worker. The WPF app owns the user experience, map interaction, preview state, and save workflow. The Node worker owns Satisfactory save parsing and serialization through `@etothepii/satisfactory-file-parser`.

The current implementation expects save files where Satisfactory has serialized mutable resource-node data. The tested path is to create a world with resource-node randomization and purity randomization enabled. Default-world saves do not contain the mutable resource and purity overrides needed for safe editing.

Generated saves use the `_NODE_EDITOR.sav` suffix by default. The original input save is never overwritten by the app.

## Features

- Load a Satisfactory `.sav` file by file picker or drag and drop.
- Use the bundled template save for a quick start.
- Show resource nodes, fracking wells, and geysers on a local Satisfactory map image.
- Pan and zoom the map while keeping node markers readable at different zoom levels.
- Filter the map by resource type, purity, and special node category.
- Click nodes to inspect their world coordinates, resource type, purity, and editable fields.
- Shuffle ordinary resource nodes into same-resource regions with an adjustable clustering slider.
- Enable hard mode at maximum clustering to separate common resources more aggressively.
- Change requested resource counts before shuffling.
- Shuffle purities separately using current, native, global, or per-resource distributions.
- Compare the visible map resource balance with the Equalizer overlay.
- Paint resource nodes directly on the map with a brush.
- Mark nodes as empty so they are removed from the final resource distribution.
- Edit fracking well resource type and satellite purities from the map card.
- Geysers are display-only because mutable geyser purity data is not available.
- Copy and clear diagnostic logs from inside the app.
- Open the generated output directory after saving.
- Show compatibility guidance when a save cannot be edited safely.

## User Workflow

1. Create or choose a Satisfactory save.
2. Save should be created with resource-node randomization and resource-node purity randomization enabled.
3. Open Satisfactory Node Editor.
4. Drop the `.sav` file on the welcome screen, choose it manually, or use the bundled template.
5. Review the detected nodes on the map.
6. Use the shuffle tab, purity controls, direct node editor, or map brush to create the desired layout.
7. Click `Save`.
8. Choose an output location. The default output name ends in `_NODE_EDITOR.sav`.
9. Load the generated save in Satisfactory.

## Architecture Overview

```text
SatisfactoryNodeEditor.sln
+-- src/SatisfactoryNodeEditor.App
|   +-- WPF application shell
|   +-- MVVM view models
|   +-- custom map and purity controls
|   +-- save inspection and mutation service boundaries
|   +-- bundled map, guide, brand, resource, and template assets
+-- src/SaveWorker
|   +-- inspect-nodes.js
|   +-- mutate-save.js
|   +-- @etothepii/satisfactory-file-parser dependency
+-- docs
    +-- save-format-notes.md
    +-- future-map-editor-design.md
```

The architecture intentionally separates the WPF app from save-file internals. The UI depends on service interfaces and DTO-shaped view models. Save parsing and writing happen out of process in the Node worker. This keeps the desktop app stable while the save backend evolves, and it allows a future pure C# backend, helper-mod backend, or config-generation backend to replace the worker without rewriting the map experience.

## Application Layer

`SatisfactoryNodeEditor.App` is a .NET WPF app targeting `net10.0-windows`.

Important components:

- `MainWindow.xaml` defines the main shell, welcome screen, compatibility guide, editor tabs, map layout, log panel, and primary commands.
- `MainViewModel` owns application state, command availability, loaded save paths, resource counts, purity distribution settings, brush settings, logs, and save orchestration.
- `MapViewport` renders the Satisfactory map, node markers, filters, details cards, pan and zoom behavior, and brush editing.
- `ThreeWayPuritySlider` edits impure, normal, and pure percentages as a constrained distribution.
- `ResourceNodeShuffleService` creates in-memory previews for resource shuffling and purity shuffling.
- `ResourceNodeInspectionService` starts the inspection worker and converts extracted world coordinates into map coordinates.
- `ExternalSaveMutationService` writes temporary assignment JSON and starts the mutation worker when the user saves.
- `SatisfactoryMapCoordinateConverter` maps Satisfactory world coordinates onto the bundled map image using fixed world bounds.
- `WindowThemeService`, `WindowsThemePalette`, and `WindowsAccentPalette` apply Windows-aware theming.

The UI never writes directly to a save while the user is previewing edits. It mutates the in-memory `ResourceNodeViewModel` collection, marks the preview as unsaved, and only calls the save service after the user clicks `Save`.

## Save Worker Layer

`src/SaveWorker` is a CommonJS Node.js package. It is currently a pragmatic bridge to the Satisfactory save parser ecosystem.

Worker commands:

```powershell
node inspect-nodes.js "C:\Path\Input.sav" "C:\Path\nodes.json"
node mutate-save.js apply-assignments "C:\Path\Input.sav" "C:\Path\Output_NODE_EDITOR.sav" "C:\Path\assignments.json"
```

`inspect-nodes.js` parses a save and writes resource-node records for the WPF app. The records include IDs, node kind, resource type, purity, world coordinates, and well satellite information where available.

`mutate-save.js` reads assignment JSON from the WPF app, applies the requested resource and purity values to serialized save objects, and writes a new save file. It prints structured JSON to stdout for the C# service and diagnostic logs to stderr.

If no candidate nodes can be changed, the worker may write `save-debug-shape.json` next to the requested output path. That file is for parser and save-shape investigation.

## Editing Model

The app distinguishes between several node kinds:

- Ordinary resource nodes are editable and participate in shuffle, purity shuffle, map brush, and direct node-card edits.
- Fracking wells are shown separately. The current UI can edit the well resource type and satellite purities, but shuffles and map brush edits ignore wells.
- Geysers are shown for context and filtering, but remain display-only.
- Empty ordinary nodes represent resource nodes that should not receive a resource assignment in the final distribution.

The shuffle algorithm works in preview memory. It groups ordinary nodes by requested resource counts, chooses balanced spatial clusters, assigns resource and purity pools into those clusters, and reports diagnostic metrics. The clustering slider moves from broad spread to compact same-resource regions. Hard mode gives selected common resources more separated seeds when the slider is maxed.

Purity handling supports four modes:

- Current: reuse the purity distribution currently visible on the map.
- Native: reuse the purity distribution captured when the save was loaded.
- Global: apply one impure/normal/pure percentage distribution across all ordinary non-empty nodes.
- Per resource: apply separate purity distributions for each resource type.

## Local Development Requirements

Install these before building locally:

- Windows 10 or Windows 11.
- .NET SDK with Windows Desktop workload support and `net10.0-windows` support.
- Node.js 20 or newer.
- npm, included with Node.js.
- Git.
- Visual Studio with a compatible .NET SDK, JetBrains Rider, or another editor that supports WPF projects.

The app starts Node by invoking `node` from `PATH`, so local development and packaged builds both need a Node runtime available unless the worker is bundled differently for release.

## Setup

From the repository root, install the save worker dependency:

```powershell
cd .\src\SaveWorker
npm install
```

Build the solution:

```powershell
cd ..\..
dotnet build .\SatisfactoryNodeEditor.sln
```

Run the app:

```powershell
dotnet run --project .\src\SatisfactoryNodeEditor.App\SatisfactoryNodeEditor.App.csproj
```

## Manual Test Flow

Use this flow for regression testing before creating any PRs:

1. Build the solution from a clean checkout.
2. Confirm `src/SaveWorker/node_modules` is installed locally.
3. Start the app.
4. Load the bundled template and confirm the map appears.
5. Load a real compatible `.sav` file.
6. Confirm ordinary nodes, wells, and geysers are visible.
7. Pan and zoom the map.
8. Toggle legend filters for resources, purities, wells, geysers, and empty nodes.
9. Click ordinary resource nodes and edit resource and purity values.
10. Click a fracking well and edit the well resource and satellite purities.
11. Confirm geysers are visible but display-only.
12. Use the shuffle tab with several clustering values.
13. Try hard mode at maximum clustering.
14. Change resource counts and confirm totals update.
15. Use native, global, and per-resource purity modes.
16. Use the map brush with Ctrl plus left mouse button.
17. Use Ctrl plus right mouse button to mark ordinary nodes empty.
18. Middle-click a node to pick brush resource and purity.
19. Save to a new output file.
20. Load the generated save in Satisfactory and inspect known nodes.

## Packaging Notes

The `.csproj` copies these runtime assets to the app output:

- `SaveWorker` files, excluding `node_modules`.
- Satisfactory map image.
- Resource icons.
- Brand icon and logo.
- Compatibility guide images.
- Bundled template save.

## Troubleshooting

If the app says the save cannot be edited, create a new Satisfactory save with resource-node randomization and resource-node purity randomization enabled, then save in-game and load that file.

If the worker fails to start, confirm Node.js is installed and `node --version` works in PowerShell.

If dependency errors mention `@etothepii/satisfactory-file-parser`, run `npm install` in `src/SaveWorker`.

If generated saves load but expected node changes are missing, inspect the app log and any generated `save-debug-shape.json` file.

If parser errors mention an offset outside the `DataView` bounds, load the save in Satisfactory, save it again, and retry with the freshly written file. This usually happens if the save file was altered (e.g. using SCIM editor) and then was saved incorrectly.

## Supporting Documentation

- `docs/save-format-notes.md` records save parser observations and risks.
- `docs/future-map-editor-design.md` describes the intended long-term editing direction.
- `CHANGELOG.md` lists release notes by version.
- `src/SaveWorker/README.md` documents direct worker usage.

## Safety Principles

- Never overwrite the selected input save.
- Keep preview edits in memory until the user saves.
- Preserve useful diagnostic logs for parser and save-shape failures.
- Keep the WPF/service boundary stable so the save backend can be replaced safely.
