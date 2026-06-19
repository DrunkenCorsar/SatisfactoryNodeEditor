# SaveWorker

Temporary Node.js save mutation worker for the Satisfactory Node Editor PoC.

Install dependencies from this folder:

```powershell
npm install
```

Apply a preview assignment file manually:

```powershell
node mutate-save.js apply-assignments "C:\Path\Input.sav" "C:\Path\Input_NODE_EDITOR.sav" "C:\Path\assignments.json"
```

The worker prints structured JSON to stdout and progress logs to stderr. If no candidate nodes are changed, it writes `save-debug-shape.json` beside the requested output save.

Inspect resource nodes for map visualization:

```powershell
node inspect-nodes.js "C:\Path\Input.sav" "C:\Path\nodes.json"
```
