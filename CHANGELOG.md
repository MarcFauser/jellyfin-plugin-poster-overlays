# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) with one deviation: the
major version encodes the Jellyfin line the build targets (`11.x` for Jellyfin 10.11, `12.x`
for Jellyfin 12), so both lines carry the same feature set under different majors.

## [Unreleased]

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
