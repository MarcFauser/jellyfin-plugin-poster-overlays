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

## 2026-08-23 — the recovery, and the repair task that was not needed for it

The 439 double-badged items had to be put right. A repair task was written for exactly that and
published as 11.7.0.0. Then the user asked the obvious question: *could you not simply have sent
those films a metadata refresh over the API?*

Yes. And it is worse than "could have": there is a more precise pair of calls than a refresh, and
the repair task is a reimplementation of them.

```
GET  /Items/{id}/RemoteImages?type=Primary
POST /Items/{id}/RemoteImages/Download?type=Primary&imageUrl=...
```

Tried on one film first — Avatar in its extended folder, whose poster carried a visible `EXT` in
each of two corners after the corner setting changed. Two calls, HTTP 204, and three seconds
later the plugin's own log:

```
14:44:27  Poster overlays: "Avatar - Aufbruch nach Pandora" was CoverReplaced after its image changed.
```

One `EXT`, in the configured corner. **The poisoned cache heals itself**: the watcher sees an
image it did not make, caches that as the new original and draws once. No plugin update, no
task, nothing to forget by hand.

The full run followed over 450 items — the badge-worthy set recomputed from the library, eleven
wider than the 439 of the day before, which is the safe direction: a film too many costs a
download, a film too few stays broken. 414 got a fresh cover, 0 failed, and the watcher logged
289 `CoverReplaced` and 127 `FirstRun` behind it with no warning. Verified by looking rather than
counting: twelve posters spread across the log, then a corner-by-corner comparison of forty
against their provider originals with the badge corner as its own positive control. The four the
comparison flagged were bright poster content, not badges — settled by cropping the strips.

**36 items could not be repaired and do not need to be.** They have no provider match, so their
"poster" is a frame Jellyfin extracted from the video, and there is no remote image to fetch.
They each still show exactly one badge, because they were never redrawn after the corner changed
and their two stacks remain superimposed. Their cache is poisoned, so a look change before their
metadata is fixed would double them — but the moment their metadata match is repaired, a real
poster arrives, the watcher sees a foreign image, and they heal on the same path as the others.

So the repair task stays, for a server whose owner cannot script two HTTP calls, and because
`NeedsRepair` is the only thing that knows which items are in scope. It was not what fixed this
library. Worth remembering before writing the next piece of recovery machinery: the host may
already expose the operation, and more precisely than the reimplementation.

## 2026-08-24 — an empty value looks like a decision

The two completeness colours came back as a bug report that named the wrong culprit, and so did
my first reading of it: the swatches showed black and picking a colour appeared to do nothing, so
the picker was suspect. The picker was fine. The presets held empty strings, written by the
load-time clobber of the release before — and an `<input type="color">` with no value renders
`#000000`. Nothing was broken at the point where it was visible.

That is the shape worth keeping: **an absence and a choice can look identical**, and when they do,
the absence wins the argument because it is the one that renders. The same defect had already
appeared twice in this project under other names — a preset with no name became invisible rather
than reported, a dangling preset id fell back silently. Each time the fix was the same: make the
blank state impossible to reach, or impossible to mistake.

Filling the default back in was the obvious repair and the one I nearly did not dare make. Writing
a value into a preset changes the look key, the look key decides whether an item is redrawn, and a
redraw starts from the cached original — of which thirty still carry a badge from the faulty first
release. The way out was not to be careful with the write but to make the write irrelevant: every
reader of a colour now goes through one accessor that treats blank as the default, the look key
included, so blank and `#3ED682` had already been producing the same key. The repair is free by
construction rather than by inspection. The test that pins it fails the moment the key reads the
raw property, which is how I know it is a guard and not a claim.

Two smaller things, both the same lesson in miniature. The "doubled" preview was the two halves of
the completeness setting shown without captions — the difference between them *is* the setting,
and it was left to be inferred. And the step buttons are centred by flex rather than by line
height, because `+` and `−` do not share an ink height; a centred box is not centred ink, which
this project learned once already on a different control.

One note about measuring. There is no Node here, so the page script was parsed with the Windows
JScript engine, which is ES3 and rejects `.catch(` as a property name. The first run reported a
syntax error in code that has been shipping and working for weeks. **A tool that reports a fault
in known-good code is describing itself**, and the honest response is to fix the probe rather than
the subject; the positive control — a deliberately broken copy — was what made the difference
visible instead of leaving both runs looking equally red.
## 2026-08-24 — the anchor that was not a guard

Two pieces of work that had been sitting on the list, and the first one arrived with its own
answer already written down. The design note said: `SxxExx` is a hard delimiter, so everything
after it is release zone — "arguably a better anchor than the title subtraction the movie parser
needs." Confident, plausible, and a **position rule**, which is the one thing `FolderNameParser`
carries three paragraphs of warning about. I had written both.

Measuring it took a minute and settled it: of twelve plausible episode titles run against the
catalogue, twelve fire a rule. *Final Cut* gives FIN, *Restored* gives REM, *Recut* gives RC — and
*The Extended Family*, an entirely ordinary title, gives EXT, because the bare word `extended` is
enough on its own. Three harmless titles fired nothing, which is what turns those twelve from an
anecdote into a measurement.

So the anchor earns its place, but not the one it was given: it marks where the *series* title
ends and nothing more. After it the episode title is subtracted exactly as a film title is, and
then one extra demand the movie parser cannot make — the remaining zone has to contain a known
release tag or it is dropped whole. That is affordable precisely *because* the anchor is hard:
after it there is nothing but title and release tags, so a zone with no tag in it is all title.
Where the movie parser widens its search when it distrusts the title, this one gives up. A missing
badge is a nuisance; a wrong one is a lie about which copy you are looking at.

**The orphan sweep is the other half, and its risk runs the opposite way.** Keeping a dead record
wastes half a megabyte. Dropping a live one destroys the only unbadged copy of that cover, so the
item keeps its badged image forever and the next run caches *that* as the original and draws on
top of it — the failure this plugin already shipped once and spent a day recovering from. So the
sweep refuses rather than trusts itself: no item found at all, or more than half the records
unmatched, and it does nothing but say so. Those are not a tuned threshold, they are the two
shapes a lookup failure takes.

Both guards were then made to fail on purpose, which is the only reason it is worth saying they
pass. Removing the title subtraction turned exactly the six title tests red and nothing else;
hard-coding the refusal to false turned exactly the four refusal tests red. A guard that has only
ever been observed not firing is a claim.

A note on the shape of the fix, from the neighbouring session that had proposed keying by path
instead of by GUID and then withdrew it: *where every key has a lifetime, the answer is not a
different key but a reconciliation.* A GUID dies when an entry is recreated, a path dies on
rename, a virtual entry never had one. That sentence is why the sweep is the right build rather
than the option left over after the others failed.