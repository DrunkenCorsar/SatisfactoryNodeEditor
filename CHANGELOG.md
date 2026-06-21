# Changelog

## 0.2.0

### New Features

- Added the map Equalizer overlay for comparing resource balance against target distributions.
- Added Native and Default Equalizer modes.
- Added weighted Equalizer calculations using ordinary node purity weights and fracking well satellite weights.
- Added an Equalizer well toggle to include or exclude fracking wells from balance calculations.
- Added an Equalizer visibility toggle for unchanged and non-actionable resources.
- Added automatic hiding for the Equalizer when all displayed resource deviations are balanced.
- Added automatic hiding for the Native/Default Equalizer switch when both target distributions are equivalent.
- Added separate purity shuffle modes for Current and Native distributions.
- Made Current purity distribution the default shuffle mode.
- Captured Native purity distribution from the loaded save so it remains distinct from later preview edits.

### Bug Fixes

- Fixed initial map positioning so a newly loaded map starts at the same location as the Fit to Map button.
- Fixed Equalizer overlay width changes when collapsing, expanding, or switching modes.
- Fixed Equalizer bar alignment so the background track and colored fill share the same left edge.
- Fixed Equalizer refresh during shuffle by dispatching live node updates back to the UI thread.
- Fixed Native purity shuffle so it preserves purity composition per resource instead of mixing a global purity pool.
- Fixed full resource shuffle with native purity settings so requested resource groups receive their own native per-resource purity composition.
