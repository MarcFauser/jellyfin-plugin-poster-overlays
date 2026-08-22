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
- Test suite: 67 cases, every folder name either taken from a real library or verified as a
  real film title that would collide.
