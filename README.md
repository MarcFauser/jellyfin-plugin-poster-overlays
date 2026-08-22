# Jellyfin Poster Overlays

Draws a small badge onto an item's primary image — the edition, the resolution, the video range —
so that two entries of the same film can be told apart **on the tile**.

```
┌──────────────────────┐
│                ┌────┐│   EXT      the cut, read from the folder name
│                │EXT ││   4K       the resolution, read from the video stream
│                ├────┤│   DV HDR   the video range, read from the video stream
│                │ 4K ││
│                ├────┤│
│                │DV  ││
│                │HDR ││
│                └────┘│
└──────────────────────┘
```

## Why

A library holds the theatrical and the extended cut of a film as two folders. Both entries carry
the same title and the same poster, and the card title is truncated to one line, so appending
"(Extended)" to the title is invisible in the grid.

Jellyfin's own answer is to merge the two into alternate versions — which hides one entry behind a
picker inside the other. If you want both tiles side by side, the marker has to go onto the image.

## The part that is easy to get wrong

**The badge is not meant to survive.** When a provider delivers a new cover, the badge is gone —
and the plugin has to notice and put it back. That makes this an upkeep loop, not a one-shot
import:

- the untouched cover is cached in the plugin's data folder;
- the image the plugin uploaded is hashed;
- a run compares the current image against that hash.

The image tag cannot be used for this: it changes when the plugin uploads too, so it cannot tell
*a provider replaced the cover* from *we badged it*. Comparing the bytes can.

Redrawing always starts from the cached original, never from the image on the item. That is what
keeps badges from stacking on badges — and stacking does not undo itself.

## Installing

Add this repository under **Dashboard → Plugins → Repositories**:

```
https://raw.githubusercontent.com/MarcFauser/jellyfin-plugin-poster-overlays/main/manifest.json
```

Then install "Poster Overlays" from the catalogue and restart the server. Version `11.x` is built
for Jellyfin 10.11 and version `12.x` for Jellyfin 12; the server picks the one matching its ABI.

Or install by hand: download the ZIP from the releases page and extract it into
`<ProgramDataPath>/plugins/Poster Overlays_<version>/`. `GET /System/Info` reports the
`ProgramDataPath`.

## First run

**Leave "Dry run" on for the first pass.** It works everything out, including the drawing, and
then throws it away: no upload, no file, no recorded state. The scheduled task log then tells you
what it *would* have changed, item by item, and the run can be repeated as often as you like.

Once the log looks right, turn the dry run off, badge a single item from the settings page, look
at it, and only then let the task loose on the whole library.

## Running it for one item

The item context menu in the web client is built from a fixed list in `itemContextMenu.js` and
cannot be extended by a server plugin, so there is no "badge this now" entry in the `…` menu.
There are two ways around it:

- **Refresh metadata** on the item. With "Re-badge as soon as an image changes" on, the plugin
  reacts to the image change and the badge is back within seconds. This is the closest thing to
  the button, and it uses an entry that already exists.
- **The settings page** has an item-id box with *Badge this item now* and *Restore its original*,
  which call:

```
POST /PosterOverlays/Apply/{itemId}
POST /PosterOverlays/Restore/{itemId}
GET  /PosterOverlays/Status
```

All three require an elevated API key.

## Removing it again

Run the scheduled task **Remove poster overlays** before uninstalling. It puts every cached
original back. Uninstalling the plugin on its own leaves the badged images exactly where they are —
they were uploaded to Jellyfin, and nothing removes them afterwards.

## Where the badges come from

| Badge | Source |
|---|---|
| Edition (`EXT`, `DC`, `UC`, `IMAX`, `THR`, `REM`, …) | the **folder name** |
| Resolution (`4K`, `8K`, …) | the video stream's pixel width |
| Video range (`DV`, `HDR`, `HDR+`, `HLG`, `DV HDR`) | the video stream's `VideoRangeType` |
| Source quality (`CAM`, `TS`, `TC`, `SCR`) | the folder name — these mark a placeholder rip |

The folder name is the only source for the edition. Not the file name, and above all not the item
name: Jellyfin's own `CleanStrings` strips `dc`, `se`, `unrated`, `4k`, `hdr` and every bracketed
suffix before a plugin ever sees a name.

The technical badges deliberately do **not** come from the folder name even though it usually
mentions them. Measured on a library of 2378 movies: the streams report 288 entries with Dolby
Vision or HDR where the folder names report 229, and one 1080p entry claims `UHD.BluRay.DV.HDR` in
its name. Where the two disagree the plugin logs it rather than picking a winner.

### How an edition is recognised

The guard against false positives is **title subtraction**, not a position rule. Films actually
called *Uncut Gems*, *The Final Cut*, *Black and White* or *Director's Cut* exist, and in all of
them the token sits directly in front of the year — exactly where a position rule would accept it.
So the item's title is removed from the front of the folder name first and only the remainder is
searched. It is the same trick Jellyfin uses in `IsEligibleForMultiVersion`.

Restricting the search to the text *after* the year, which is the obvious rule, was measured
against a real library and rejected: 70 of 168 edition tokens sit **before** the year there, so
that rule would lose 43 per cent of them.

Short tokens (`DC`, `SE`, `OM`, `BW`, `CHRONO`) are additionally only accepted when the folder
spells them in capitals, because that is what separates a release tag from a word.

If an item has no metadata match at all — its name *is* its folder name — subtraction would eat
everything, so the parser notices and falls back to searching the whole name.

## Settings

Everything is configurable, and every size is a percentage of the image rather than a pixel count,
so one setting looks the same on a 600×900 and a 1000×1500 master. The defaults were chosen on
rendered comparisons at real card size, not guessed.

The resolution ladder is a setting for a reason: only 4K and 8K have standardised widths.
Everything above is extrapolation, so it lives in a text field instead of in the code.

## What it does not do

- **Series are not badged yet.** Two release folders of one series produce two `Series` rows that
  Jellyfin collapses at query time by picking one of them for the tile. On the reference library
  one series existed as 14 rows behind a single tile. Which row supplies the image is not knowable
  from outside, so this needs its own design rather than a guess.
- **Some duplicates cannot be told apart by any badge.** Of 109 groups sharing a TMDB id on the
  reference library, 32 differed only in resolution, 20 in the edition, and **57 in nothing the
  plugin can see** — codec, audio, or they were duplicates that should not both exist. The task
  log lists them instead of pretending otherwise.
- **If the plugin's data folder is lost while images are badged**, the cached originals are gone
  and the badged images look like originals to a fresh run. Run *Remove poster overlays* before
  moving or clearing that folder.

## Building

```
dotnet build Jellyfin.Plugin.PosterOverlays.slnx -c Release
dotnet test  tests/Jellyfin.Plugin.PosterOverlays.Tests
./build.ps1
```

`build.ps1` publishes both target frameworks, writes the `meta.json` the server reads, packs a ZIP
per Jellyfin line and updates `manifest.json`. Add `-Publish -Changelog '…'` to create the GitHub
releases and push the manifest; it refuses to replace an already published version.

## Licence

AGPL-3.0. The embedded typeface is [Inter](https://github.com/rsms/inter) under the SIL Open Font
License; its licence text ships inside the assembly and is in
[`Resources/Inter-LICENSE.txt`](Jellyfin.Plugin.PosterOverlays/Resources/Inter-LICENSE.txt).
