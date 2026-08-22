# History

A narrative log of how this project came to be. The structured, versioned list of changes
lives in CHANGELOG.md; this file records the reasoning — including the alternatives that were
rejected and why, because that is the part nobody can reconstruct later.

## 2026-08-22 — why the plugin exists

A library holds the theatrical and the extended cut of a film as two folders. Both entries
carry the same title and the same poster, and the card title is truncated to one line, so
appending "(Extended)" to the title is invisible in the grid.

Jellyfin's own answer is to merge the two into alternate versions. **That was rejected on
purpose**: it hides one entry behind a picker inside the other, and both tiles should stay
side by side. So the marker has to go onto the image itself.

The shaping requirement, and it is the opposite of what one would assume:

> The overlay is **not** supposed to survive. When a provider delivers a new cover, the badge
> should be gone — but the plugin must notice that a new cover arrived and put the badge back.

That makes this an upkeep loop, not a one-shot import: cache the original, hash what was
uploaded, compare, re-apply.

### What was measured before anything was designed

Against the live library (2378 movies, 1580 series rows, Jellyfin 10.11.11), read-only:

| Question | Answer |
|---|---|
| How many films exist more than once under one TMDB id? | 109 groups, 228 entries |
| What distinguishes them? | 32 resolution folder only, 10 edition only, 10 both, **57 nothing** |
| Which edition words actually occur? | extended 45, uncut 45, remastered 34, unrated 16, imax 13, bare `DC` 6, `directors cut` 6, special edition 4, open matte 4, kinofassung 3, langfassung 2, theatrical 1 |
| Where does the edition token sit? | **70× before the year, 96× after, never both** |
| Is the folder name a usable source for HDR/DV? | No. Streams say 288, the folder name says 229; 60 are invisible in the name, and one 1080p entry has `UHD.BluRay.DV.HDR` in its name |
| How many films have no metadata match at all? | 186 (`Name` equals the folder name) |

The 57 undistinguishable groups differ only in codec and audio (`dts` 20, `x264` 17, `x265`
17, `ac3` 14) or are mismatches. No badge can separate them; they are reported in the task
log instead of being papered over.

### Decisions that follow from those numbers

- **The edition comes from the folder name only** — never the file name, and never the item
  name: Jellyfin's `CleanStrings` strips `dc`, `se`, `unrated`, `4k`, `hdr` and every
  bracketed suffix from the name before a plugin ever sees it.
- **Title subtraction is the primary guard, the year anchor only the second.** Research
  proposed "match only after the last year, no year no badge". Measured against this library
  that rule costs 72 of 168 badges, including the film the whole problem was explained with.
  Instead the item title is subtracted from the folder name first and only the remainder is
  searched — the same trick Jellyfin uses in `IsEligibleForMultiVersion`. It also kills the
  real collisions: films actually called *Uncut Gems*, *The Final Cut*, *Director's Cut*.
- **A fallback is required** because 186 items have no metadata: when the title consumes the
  whole folder name, the title is worthless and the year anchor takes over. Without this the
  guard silently suppressed every badge — and looked like a success while doing it.
- **Technical badges come from `MediaStreams`**, with the folder name as a cross-check whose
  disagreements get reported rather than hidden.
- **Series are postponed.** Two release folders produce two `Series` rows that Jellyfin
  collapses at query time by picking an arbitrary group representative. Archer exists as 14
  rows and one tile. Since the row that supplies the tile is not knowable, either all rows of
  a group get the badge or none — that needs its own design round.

### Scaffolding

Taken from the sibling plugin `Jellyfin.Plugin.JFLint` rather than from the upstream
template, which still pins Jellyfin.Controller 10.9.11: the multi-targeted project file, the
`jellyfin.ruleset`, AGPL-3.0, the versioned pre-commit hook that enforces a changelog entry,
and `install-git-hooks.ps1` (byte-identical, SHA256 `3D948270BBA1378F…`). The GUID is freshly
generated — reusing the other plugin's would let Jellyfin treat the two as the same plugin.

SkiaSharp is referenced compile-only at the version the server itself carries: 3.116.1 for
Jellyfin 10.11, 3.119.4 for Jellyfin 12, both read from `Directory.Packages.props` of the
respective branch. Shipping a copy would load a second SkiaSharp into the plugin's own
assembly load context, with its own native handles.

## 2026-08-22, later — the first run on a real library, and what it broke

The badges themselves were right on the first attempt: two entries of one film became
distinguishable on the tile, which is the whole point. Everything around them was not.

**The settings page could not be saved, and said nothing.** The style and corner selects used
numeric option values while the server sends and expects the enum names, so on load nothing
matched and the select fell back to its first entry; an empty number field became `NaN`, which
JSON turns into `null`, which the server refuses; and there was no error handler, so a rejected
save showed up as a loading indicator that spun forever. The user therefore could not switch the
dry run on — and believed he had.

**So the first run was a real one.** 2372 items, 439 badged, 7 minutes 51.

**And 417 of those 439 were badged twice.** The scheduled task and the image-change watcher each
built their own state store, and the task only wrote its records to disk when the whole run had
finished. The watcher, woken by an upload the task had just made, read an empty file, concluded
it had never seen the item, cached the freshly badged image as the "original", and drew a second
badge on top. Two identical stacks land on the same pixels, so nothing looked wrong at all.

That is the part worth remembering. The claim in the previous section — *it cannot loop, because
the second pass finds the hash it just recorded* — was stated twice with confidence and was only
half true. Idempotence protects nothing when the two sides read different books. The fix is one
shared store, a write to disk after every single item, and a per-item claim the watcher honours
while the applier still has the item open.

**A badged image cannot be un-badged**, so for the affected items the cached "original" is lost
and the only true original left is the one the provider still has. Hence two more things: a
check that refuses to draw on or restore from a cache whose hash no longer matches its record,
and a repair task that fetches a fresh primary image for exactly those items.
