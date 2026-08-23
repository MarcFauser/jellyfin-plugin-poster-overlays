# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) with one deviation: the
major version encodes the Jellyfin line the build targets (`11.x` for Jellyfin 10.11, `12.x`
for Jellyfin 12), so both lines carry the same feature set under different majors.

## [Unreleased]

### Changed

- **"Repair poster overlays" no longer looks for an inconsistent cache, because that cannot be
  found.** A cache written by the faulty first release is perfectly consistent with its own
  record - it merely describes an image that already carries a badge. The old check reported
  "319 records, 319 intact" on a library where all 319 were wrong, and the damage only became
  visible when the corner setting changed and the second badge appeared somewhere else on the
  poster. The task now works on scope rather than consistency: every item the plugin has a
  record for, plus every item it would badge, which together are exactly the set the broken run
  touched. For each it fetches a fresh primary image from the provider and forgets what it
  remembered. Run "Apply poster overlays" afterwards to badge them once, cleanly.
- `POST /PosterOverlays/Repair/{itemId}` does the same for a single item, so the fix can be
  tried on one poster first.

### Fixed

- **Everything that gets written now uses the invariant culture explicitly.** `build.ps1`
  formatted the `meta.json` timestamp with `'yyyy-MM-ddTHH:mm:ssZ'` and no culture. In a .NET
  custom format string a bare colon does not mean a colon, it means *the current culture's time
  separator* - and 21 cultures use a full stop, which would have persisted
  `2026-08-23T14.05.07Z`. It was only correct here by accident. The script now runs invariant
  throughout, the two persistence points say so explicitly, the separators are escaped, and a
  check refuses to continue if the timestamp is not ISO 8601 UTC. The same escaping went into
  the state file's timestamp, with a test that runs under `da-DK`.
- **`build.ps1` no longer blanks the changelog of an already published version** when a run
  supplies none. The checksum guard below does not catch this, and cannot: an unchanged source
  produces a byte-identical artifact, so the checksum matches and the entry is rewritten anyway.
  Reproduced on an untouched 11.6.0.0 - same MD5, changelog from 323 characters to zero. Unlike
  a wrong checksum, which aborts the install with an error, an emptied catalogue entry is
  silent. The published text is now kept and the run says so; supplying `-Changelog` still
  overwrites it, which was checked in both directions. Found by the sibling plugin's session
  while it was fixing the two below.
- **`build.ps1` no longer rewrites the manifest entry of a version that already exists** with a
  different checksum. A local build without `-Publish` did exactly that after a source change
  without a version bump, leaving the repository advertising a checksum the published release
  does not have. It now stops and says to raise the version.

- **Changing how the badges look now actually redraws them.** Only the badge *set* was compared,
  so switching the style, the corner, the direction or any size left every already badged poster
  reported as "already correct" and untouched - the new setting only reached items that happened
  to need a new badge anyway. A second key now records what the poster was drawn with, and a
  change to any of those settings redraws from the cached original. Settings that do not change
  the pixels are deliberately left out of it, so saving an exclusion list does not order a redraw
  of the whole library.

### Fixed (settings page)

- **The settings page did not work at all.** Its root element carried
  `data-controller="__plugin/posteroverlays"`, pointing at a module that does not exist, where
  the official template carries `data-require="emby-input,emby-button,emby-select,emby-checkbox"`.
  That one attribute is what makes Jellyfin register the controls and dispatch `pageshow`, so
  nothing was upgraded - textareas rendered as raw boxes, the checkbox layout collapsed - and no
  value was ever loaded into any field.

### Added

- A **3D badge**, in a category of its own rather than as an edition: a film can be an extended
  cut *and* in 3D, and one must not push the other off the poster. Recognises `3D`, `HSBS`,
  `Half-SBS`, `MVC`, `HTAB` and, in capitals only, the ambiguous `SBS` and `TAB`.
- **Direction**: the badges can run along the top or bottom edge in one row instead of stacking
  downwards. Corner says where they start, direction says which way they grow.
- **A live preview** on the settings page: all three styles drawn side by side from the current
  geometry, corner, direction and order. Clicking one selects it - the preview *is* the style
  picker, so there is no dropdown repeating it.

### Changed (settings page layout)

- Number fields are narrow and right aligned instead of stretching across the page, with room
  between the digits and the spinner arrows - right alignment alone puts the value flush against
  the up/down control. The eleven copies of that inline style are now one scoped rule, so the next
  adjustment is one edit rather than eleven.
- The exception boxes are full-width textareas with their label above them, and the checkbox
  rows lay themselves out. None of it depends on the `emby-*` custom elements any more: those
  are registered by whichever page imported their module, and a plugin configuration page
  imports nothing - `emby-input` and `emby-select` happen to be available, `emby-textarea` is
  not, which is why those boxes rendered as 20-column stubs wedged beside their labels.

### Fixed (earlier in this version)

- **Badges could be drawn twice.** The scheduled task and the image-change watcher each kept
  their own state, and the task only wrote its records to disk when the whole run had finished.
  So the watcher, reacting to an upload the task had just made, read an empty file, concluded it
  had never seen the item, cached the freshly badged image as the "original" and drew a second
  badge over it. Two identical stacks land on the same pixels and are invisible, which is what
  made it dangerous rather than merely wrong: nothing looked broken, but the cached original was
  no longer an original and every later run would have added another layer. Measured on a real
  library: 417 of 439 badged items in one run. There is now one shared store, a write to disk
  after every item, and a per-item claim the watcher respects while the applier holds it.
- **A damaged cache is now detected and stops the run for that item.** If the cached original no
  longer hashes to what the record says, nothing is drawn and nothing is restored - a layer does
  not come off again. The new "Repair poster overlays" task collects those items and fetches a
  fresh primary image from the metadata provider, which is the only true original left. It
  respects the dry run switch.
- **The settings page could not be saved.** Three faults at once: the style and corner selects
  used numeric option values while the server sends and expects the enum names, so the selects
  silently fell back to their first entry; an empty number field became `NaN`, which JSON turns
  into `null`, which the server refuses; and the save had no error handler, so the failure showed
  up as a loading indicator that never stopped. It now reports what went wrong instead.

### Added

- Project scaffolding: multi-targeted `net9.0` / `net10.0` project, Jellyfin ruleset,
  AGPL-3.0 licence, versioned git hooks.
- Folder-name parser for editions and source quality, guarded by title subtraction rather
  than by a position rule, with a fallback for items that have no metadata match.
- Resolution ladder (`width / 960`, snapped to a configurable list of rungs) and video range
  mapping, both derived from the video stream rather than the folder name.
- Badge renderer using SkiaSharp with an embedded Inter SemiBold typeface, three styles, four
  corners, and every measurement expressed as a percentage of the image.
- Plugin entry point and configuration page.
- Test suite: 72 cases, every folder name either taken from a real library or verified as a
  real film title that would collide.
- The upkeep loop: the original cover is cached, the uploaded image is hashed, and a run
  compares the current image against that hash to tell "a provider replaced the cover" from
  "we badged it". Redrawing always starts from the cached original, never from the image on
  the item, so badges cannot stack.
- Two scheduled tasks: "Apply poster overlays" (daily by default, and runnable on demand from
  Jellyfin's own task screen) and "Remove poster overlays", which restores every cached
  original and is the way out before uninstalling.
- Task log reporting: how many covers a provider replaced, which groups of entries end up with
  identical badges and therefore still cannot be told apart, edition-looking phrases that no
  rule covers, and disagreements between the folder name and the video stream.
- Dry run: everything is worked out including the drawing and then thrown away, so a first run
  can be read before it changes anything.
- A watcher that re-badges an item as soon as Jellyfin reports its image changed. This makes
  "Refresh metadata" in the item's own menu a manual trigger for one film - the web client's
  context menu is a fixed list and cannot be extended by a server plugin.
- API routes `POST /PosterOverlays/Apply/{itemId}`, `POST /PosterOverlays/Restore/{itemId}` and
  `GET /PosterOverlays/Status`, plus a box on the settings page that calls them.

- `build.ps1`, `manifest.json`, a README and a catalogue logo, so the plugin can be installed
  from a repository URL instead of by hand.
- The badge order is configurable, as a list on the settings page that can be sorted with the
  arrow buttons. The order is also the priority: what falls off when there are more badges than
  the maximum is what sits at the bottom.

### Changed

- SkiaSharp is referenced with `IncludeAssets="compile"` instead of `ExcludeAssets="runtime"`.
  The managed assembly was already left out, but native assets are a separate asset group and
  travelled regardless: the first packages came out at 19 MB and 86 MB, carrying Windows and
  macOS copies of `libSkiaSharp` that a Linux server has no use for and that would shadow the
  server's own. They are now 280 KB. `build.ps1` prunes recursively and asserts on the packed
  ZIP rather than on the staging folder, because the flat prune is what let this through.
- `CA3003` is lowered to a suggestion in `.editorconfig`, matching the setting in the Jellyfin
  server's own `.editorconfig`. The analyser treats every ASP.NET route parameter as tainted
  regardless of type, so a `Guid` route reaching any file operation is reported - including
  paths Jellyfin itself produced. The guard in `OverlayStateStore.OriginalPath` stays.
