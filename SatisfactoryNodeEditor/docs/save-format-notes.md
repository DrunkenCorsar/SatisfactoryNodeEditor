# Save Format Notes

The PoC uses `@etothepii/satisfactory-file-parser` through a temporary Node.js worker.

Observed from the parser guide:

- A parsed save exposes `levels`.
- Levels contain `objects`.
- Objects may have generic `properties` and object-specific `specialProperties`.
- Save writing is performed by serializing the modified parsed object back through the parser.

The worker currently searches object names, type paths, and properties for:

- `ResourceNode`
- `ExtractableResource`
- `mResourceType`
- `mResourceClass`
- `mPurity`

Known risk: vanilla resource nodes may not exist in fresh saves as normal serialized objects. If generated saves load but nodes remain unchanged, inspect `save-debug-shape.json` to find the actual serialized shape, or confirm that a mod/runtime override path is required.

No successful in-game mutation has been verified yet in this workspace because no `.sav` sample was provided.
