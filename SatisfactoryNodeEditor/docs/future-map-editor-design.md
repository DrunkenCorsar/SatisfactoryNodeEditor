# Future Map Editor Design

The future editor should repaint existing vanilla resource nodes only.

Do not implement:

- arbitrary node placement
- node movement
- node creation
- node deletion

Allowed future edits:

- resource type
- purity, if later validated
- distribution rules, if represented as overrides rather than physical node edits

## Backend Shape

The current `Turn all nodes into Bauxite` operation should be treated as a special case of:

```text
Apply selected resource = Bauxite to every detected node
```

The future map flow can reuse the same backend boundary:

1. Load save and discover resource nodes or load known vanilla node coordinates.
2. Present nodes as `ResourceNodeDto`.
3. Let the user change `SelectedResource`.
4. Submit a `SaveMutationRequest`.
5. Write a new save or external override artifact without overwriting the original.

If save-only editing cannot reliably affect vanilla resource nodes, keep the WPF app and mutation service boundary, then replace the service implementation with a helper-mod or config-generation backend.
