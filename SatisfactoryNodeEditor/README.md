# Satisfactory Node Editor PoC

Experimental Windows desktop proof of concept for Satisfactory save mutation.

The current mutation shuffles ordinary resource nodes into same-resource spatial clusters while preserving the original resource and purity composition. Wells and geysers are visualized separately and are not shuffled. The app never overwrites the original save; generated files use the `_NODE_EDITOR.sav` suffix.

Always back up your saves before loading generated files in Satisfactory.

## Requirements

- Windows
- .NET SDK with Windows Desktop support
- Node.js 20 or newer

## Setup

Install the temporary save worker dependency:

```powershell
cd .\src\SaveWorker
npm install
```

Build the app:

```powershell
cd ..\..
dotnet build .\SatisfactoryNodeEditor.sln
```

Run the app:

```powershell
dotnet run --project .\src\SatisfactoryNodeEditor.App\SatisfactoryNodeEditor.App.csproj
```

## Manual Test Flow

1. Create or pick a Satisfactory save.
2. Back it up manually.
3. Open the app.
4. Click `Read .sav file`.
5. Confirm resource nodes appear on the map.
6. Zoom with the mouse wheel, pan by dragging, and hover nodes for resource/purity details.
7. Confirm wells use the separate diamond marker and keep water, nitrogen, or oil coloring.
8. Confirm geysers use their own fixed-color marker.
9. Click a node, well, or geyser marker and confirm the map-attached details card opens.
10. Click another marker and confirm the previous details card closes.
11. Click `Shuffle` repeatedly and confirm the map updates immediately without writing a save.
12. Click `Save` when you like the preview.
13. Load the generated `_NODE_EDITOR.sav` file in Satisfactory.
14. Check known ordinary resource node positions.

If no nodes are changed, inspect `save-debug-shape.json` beside the generated output path. This file contains a small sample of save objects with resource, purity, node, or extractor text.

## Architecture

The WPF app depends on `ISaveMutationService`, not directly on parser internals. The current implementation uses `ExternalSaveMutationService`, which starts:

```text
node mutate-save.js apply-assignments <inputSavePath> <outputSavePath> <assignmentsJsonPath>
```

The WPF app previews shuffle results in memory. The Node worker uses `@etothepii/satisfactory-file-parser` to parse and write `.sav` files only when the user clicks `Save`, applying the current preview assignments to ordinary resource-node objects.

Map visualization uses a separate inspection worker:

```text
node inspect-nodes.js <inputSavePath> <outputNodesJsonPath>
```

The app converts extracted world coordinates into map coordinates through `SatisfactoryMapCoordinateConverter`, using fixed Satisfactory world bounds rather than fitting to the visible node set. It then renders nodes over a local Satisfactory map image in a reusable WPF `MapViewport`. Node markers keep the same on-screen size while zooming. Clicking a marker opens a map-attached details card with a purity-colored header.

This separation is deliberate. A later pure C# backend can replace the worker without changing the UI flow.

## Current Status

This repository builds a Windows WPF PoC, includes a save worker, and has been tested against a real save sample. On that sample, inspection extracted 625 map records: 459 ordinary resource nodes, 135 wells, and 31 geysers. Shuffle preserves ordinary resource/purity composition and ignores wells/geysers.

## Future Goal

The future application should become a fixed-position node repainting tool: keep vanilla node locations, do not create or delete nodes, and let users choose resource types for existing nodes through an interactive map.
