# Design: presets, categories, and badges beyond movies

Status: **agreed, not built**. Written 2026-08-23 after the design was worked out against the
user's real library rather than in the abstract. Every number below was measured; where something
was not measured it says so.

The changelog records what changed. This file records what was decided and why, so the next
session does not re-derive it — and so the decisions that were *rejected* stay rejected for a
reason rather than being tried again.

---

## 1. Why this exists

Today the plugin badges movies. The request is series, and the user named the real case: a season
he owns in both 1080p and 4K DV HDR. The series appears once, the season appears once, but every
episode appears **twice in the episode list with nothing to tell the two apart**.

Measuring that turned up something the abstract description did not:

```
"The Last of Us" is THREE Series rows in the database, and SIX Season rows:

  2160p/The.Last.of.Us.2023.S02...DV.HDR.MAX...   734bc466…
  1080p/The.Last.of.Us.2023.S02...AMZN...         853d5f9f…
  1080p/The.Last.of.Us.2023.S01...VECTOR          e9c62072…
```

The library "Series" gathers **26 locations** split by resolution (`Series/1080p`, `Series/2160p`,
…), so the same show lands in several of them. Jellyfin merges them for display through the
presentation key: one tile, one season, and an episode list that is the *union* of the rows.

**Which of the three rows supplies the visible tile is Jellyfin's choice, not ours.** Measured
with a user context, the grid returns exactly one row for that show — the **1080p** one. A badge
written to "the series" therefore lands on an invisible row as often as not.

### The numbers

| | |
|---|---:|
| Series / Seasons / Episode rows | 1580 / 9659 / 29888 |
| Episode rows with a real file | 25419 |
| Episode rows that are missing-episode placeholders (no file) | 4469 |
| Episodes at 4K or above | 680 (2.7 %) |
| Episodes that have their own primary image | 25370 (84.9 %) |
| **Duplicate episodes** (same series name, same S/E) | **896 rows in 411 groups, 27 series** |
| of those, copies differ in resolution | 210 groups |
| of those, copies share a resolution | 201 groups |
| Series entirely 4K (duplicates collapsed) | 44 |
| Series partly 4K (duplicates collapsed) | **6** |

Two things follow immediately.

**Episodes are where the problem is, and where the data is unambiguous.** One episode row is one
file with one resolution and one range, and 85 % of them carry their own image. Nothing has to be
inferred.

**Series and seasons are where a badge cannot simply be read off the item**, because a series has
no resolution of its own and the visible row is not ours to pick.

### The 201 that badges cannot fix

Half the duplicates sit in the *same* resolution folder, so a technical badge would read
identically on both:

```
buck.rogers.s01e01.german.dl.1080p.fs.bluray.x264-excited.mkv
buck.rogers.s01e01e02.german.dl.alternate.cut.1080p.bluray.x264-excited.mkv
```

`alternate cut` is already in the edition catalogue. What differs is that for movies the parser
reads the **folder** name and here it would have to read the **file** name. That is a separate
piece of work, listed in section 9.

---

## 2. The two concepts

> A **preset** is a *look*. A **category** is a *policy*.

Everything else follows from keeping those apart. A preset can then serve Movie, Series and Season
at once; changing it changes all three. If policy leaked into presets, "Episode" could never be
reused for anything else.

A preset may carry settings a given category has no use for — the completeness colours mean
nothing on a movie. That is not a contradiction: **the preset describes, the category decides
whether there is anything to describe.**

### In the preset

Style, corner, direction, the eight geometry percentages, maximum badge count, badge order, and
the completeness signalling: colours on/off, both colours, glow on/off and strength, and the
partial marker (none / vertical / diagonal / wave / hatch).

### In the category

Whether it is badged at all, which preset applies, which badge kinds are allowed (a season has no
use for an edition badge), and the two special cases:

- **Series and Season**: the aggregation rule, section 5.
- **Episode**: *only where it disambiguates*, section 6.

---

## 3. Storage, and the constraint that shapes it

Jellyfin persists a plugin configuration with **`XmlSerializer`**, to
`plugins/configurations/<assembly>.xml`. Verified in
`MediaBrowser.Common/Plugins/BasePluginOfT.cs` on `release-10.11.z`:

```csharp
XmlSerializer.SerializeToFile(config, ConfigurationFilePath);
```

and, on the way back in:

```csharp
try { return (TConfigurationType)XmlSerializer.DeserializeFromFile(typeof(TConfigurationType), path); }
catch { var config = Activator.CreateInstance<TConfigurationType>();
        SaveConfiguration(config); return config; }
```

**That `catch` is the hazard of this whole design.** Any load failure — including a property type
`XmlSerializer` cannot handle — silently replaces the configuration with defaults *and writes them
back*. The user loses every setting with no message. And because the effective values change, the
look key changes, and everything gets redrawn.

Three consequences, all binding:

1. **No `Dictionary<,>` anywhere.** `XmlSerializer` cannot serialise it. Categories are four named
   properties, presets are a `List<T>`.
2. **Only types it handles**: primitives, `string`, `Guid`, enums, nullable value types, and public
   classes with a parameterless constructor and public settable properties. No interfaces, no
   polymorphism, no tuples.
3. **The plugin keeps its own backup.** On every save it writes a timestamped copy of the
   configuration into its data folder and keeps the last few. A silent reset then costs a restore
   instead of an afternoon. This is not belt-and-braces: the reset path is in the framework, it is
   unconditional, and we cannot intercept it.

A round-trip test belongs in the suite: serialise a fully populated configuration, deserialise it,
and compare. It must run against the *real* `XmlSerializer`, not a hand-rolled one — a test that
normalises its input cannot find this class of fault.

### Identity: GUIDs, not names

Presets are referenced by `Guid`, never by name.

| | with names | with ids |
|---|---|---|
| rename a preset | breaks every category pointing at it, or needs a cascade | display-only change |
| two configs both have "Compact" | one silently wins | both survive, one is shown as "Compact (2)" |
| built-in referenced from a shared config | breaks if the name is ever localised | stable |

The middle row is the important one, and it is the same silent-clobber failure that cost real work
elsewhere in this project: a name collision that resolves quietly is worse than one that is
reported.

Built-in presets have **fixed GUIDs declared as constants in code**, and they are *counted* rather
than random:

```
00000000-0000-0000-0000-000000000001   Movie
00000000-0000-0000-0000-000000000002   Series
00000000-0000-0000-0000-000000000003   Season
00000000-0000-0000-0000-000000000004   Episode
```

Collision is not a risk for values that are never generated, and the payoff is legibility: a
category pointing at `...0002` in the configuration file or in a shared export is visibly pointing
at something shipped, where a random id would be indistinguishable from one the user made.

**Numbering starts at one, and that is load-bearing.** All zeroes is `Guid.Empty`, which is also
what an unset `PresetId` holds. If that resolved to a built-in, a category nobody configured would
quietly draw with somebody's defaults instead of reporting that it has no preset - the failure
that looks like success. There is a test for it.

`Guid` was chosen over a readable string id (`builtin:movie`) deliberately: it is a type
`XmlSerializer` handles natively, comparison is exact, and there is no reserved-prefix rule for a
user to collide with by accident or on purpose. The export format carries the name next to the id
so a shared file reads well; **on import the id decides and the name is only shown.**

### The shape

```csharp
public class PluginConfiguration : BasePluginConfiguration
{
    // global behaviour - unchanged from today
    public bool Enabled { get; set; }
    public bool DryRun { get; set; }
    public bool WatchForImageChanges { get; set; }
    public bool WriteToMediaFolder { get; set; }
    public int  JpegQuality { get; set; }

    // global semantics - the same everywhere, so not per category
    public string ResolutionLadder { get; set; }
    public int    MinimumResolutionK { get; set; }
    public bool   MergeDolbyVisionAndHdr { get; set; }
    public string ExcludedItemIds { get; set; }
    public string EditionOverrides { get; set; }

    public List<BadgePreset> CustomPresets { get; set; }   // built-ins live in code

    public CategorySettings Movies   { get; set; }
    public CategorySettings Series   { get; set; }
    public CategorySettings Seasons  { get; set; }
    public CategorySettings Episodes { get; set; }
}

public class BadgePreset            // a look
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public BadgeStyle Style { get; set; }
    public BadgeCorner Corner { get; set; }
    public BadgeDirection Direction { get; set; }

    public double PillHeightPercent { get; set; }
    public double FontSizePercentOfPill { get; set; }
    public double PaddingPercentOfPill { get; set; }
    public double GapPercentOfPill { get; set; }
    public double CornerRadiusPercentOfPill { get; set; }
    public double BorderWidthPercentOfPill { get; set; }
    public double HorizontalMarginPercent { get; set; }
    public double VerticalMarginPercent { get; set; }

    public int MaxBadges { get; set; }
    public string BadgeOrder { get; set; }

    // completeness signalling; unused where the category has no such state
    public bool CompletenessColours { get; set; }
    public string UniformColour { get; set; }      // "#3ED682"
    public string PartialColour { get; set; }      // "#FFAA28"
    public bool Glow { get; set; }
    public double GlowStrengthPercentOfPill { get; set; }
    public PartialMarker PartialMarker { get; set; }   // None|Vertical|Diagonal|Wave|Hatch
}

public class CategorySettings       // a policy
{
    public bool Enabled { get; set; }
    public Guid PresetId { get; set; }

    public bool AllowEdition { get; set; }
    public bool AllowResolution { get; set; }
    public bool AllowVideoRange { get; set; }
    public bool AllowFormat { get; set; }
    public bool AllowSource { get; set; }

    public bool OnlyWhereItDisambiguates { get; set; }   // episodes
}
```

Colours are stored as `#RRGGBB` strings, not as a colour type: `XmlSerializer` has no colour
concept, and a string is legible in the file and in an export. It is parsed once and validated;
an unparseable value falls back to the built-in colour **and is reported**, never silently.

---

## 4. Built-in presets

Four ship with the plugin: **Movie**, **Series**, **Season**, **Episode**. They live in code, not
in the configuration, and are **read-only**.

`Movie` and `Episode` have the completeness colours off — a movie is one file and an episode is one
file, so everything would always be "complete", and a colour that is always the same is not
information. `Series` and `Season` have them on.

`Episode` is the landscape one. The geometry cannot be copied from the portrait presets: at a tile
width of 200 px a 2:3 poster is 300 px tall and a 5.5 % pill is 16 px, while a 16:9 still at 300 px
wide is 169 px tall and the same 5.5 % gives 9 px. The starting value for `Episode` is therefore
about **10 %**, to be checked on a rendered still rather than trusted.

**A built-in whose rendering would change gets a new id; the old one stays**, marked as
superseded. Otherwise "read-only" is only half a promise: protected against the user, but not
against us at the next release. This should be rare.

### Editing one

The fields are **disabled** while a built-in is selected, with a **Duplicate** button beside the
name. Not "editable until you try to save": the protection has to be visible before typing, not
discovered afterwards. A duplicate lands as a custom preset named `<name> (copy)` with a fresh
GUID and is immediately editable.

A custom preset may not take the name of a built-in.

---

## 5. Series and seasons: what a badge there even means

A series has no resolution. It has to be derived from its episodes, and there are **two different
kinds of mixture** that feel the same and are not:

| | |
|---|---|
| **choice of copies** | the same episode exists more than once. Nothing is missing; you simply pick |
| **genuine variation** | different episodes are genuinely different |

Measured, with the two cases side by side:

```
Lost in Space   28 episodes, EVERY one present twice: 1080p DV HDR and 4K DV HDR
                → collapse to the best copy → uniformly 4K DV HDR → a plain badge is correct

The Last of Us  16 episodes, 7 present twice (1080p SDR / 4K DV HDR)
                → collapse → S1 has nothing, S2 has 4K DV HDR → genuinely mixed
```

**The rule: collapse every episode to its best copy first, then require agreement.** Without the
collapse, *Lost in Space* would be reported as mixed, which is simply false — the whole series is
available in 4K DV HDR.

A pleasant side effect: *Danger Mouse* and the four *Es war einmal…* series are full of duplicates
but entirely SD/720p SDR. They collapse to "nothing notable" and get **no badge at all**, not a
mixed one. The mixed state only appears where something worth showing actually varies.

The state is **binary**. *Der Schwarm* is 7 of 8 and *Fire Country* is 22 of 52; both are simply
"not uniform". A threshold — "90 % counts as complete" — was considered and rejected: it would lie
at exactly the point where someone is relying on it.

Applied to the library: **44 series uniform, 6 partial.** The rare state is the conspicuous one,
which is the right way round; if it were reversed the signal would be worthless.

### Drawing it

Everything visible on a tile was tried against real posters at real size, because reasoning about
it was repeatedly wrong.

**Anything that leaves half the pill empty was rejected.** On a bright poster the text over the
empty half disappears — and guaranteeing contrast over artwork we do not control is the entire
reason the pill exists. Filling the second half in a *lighter opaque* tone instead keeps both the
contrast and the "half" reading.

**A wave-shaped boundary is invisible at tile size.** At a 16 px pill the wave is about two pixels.
It does become visible on a detail page where the poster is shown large, and it costs nothing to
draw, so it stays available as an option — but it is not the default, because it does nothing where
it would be needed.

**Colour beats form.** Green for uniform, amber for partial, with a soft glow, reads instantly
across a wall of tiles where a fill pattern has to be searched for. The glow has a second benefit:
it lifts the pill off busy artwork.

Three caveats, all real:

- The background is not ours. A green badge on a green poster loses much of its signal. It stays
  legible because the dark fill and the glow separate it, but the colour is weakened.
- **Green and amber is exactly the pair that converges under the common red-green deficiency.** The
  diagonal split is therefore kept *as well*, even though it is nearly redundant next to the
  colour: it costs nothing and it is the second channel when the first fails.
- Colour used for completeness cannot also be used for anything else later, e.g. to distinguish
  badge kinds. That door closes here.

---

## 6. Episodes

One row, one file: no aggregation, no colour. The open question is *which* episodes get a badge.

- **Everything notable**, as movies are treated today: consistent, but that is thousands of
  thumbnails carrying a badge that answers a question nobody asked.
- **Only where it disambiguates** — an episode that has a twin: **896 rows**, the same order of
  magnitude as the 414 badged movies.

The default is *only where it disambiguates*, with the other available as a switch. The reason is
what the badge is for: on a poster wall a badge helps you choose, but in an episode list you
already know which episode you want. The only open question there is *which copy*.

The 4469 placeholder rows for missing episodes have no file and mostly no image. They are skipped.

---

## 7. The settings page

Five sections, no tabs — the categories pick a preset from a list, which is what tabs would have
been for:

1. **Operation** — enabled, dry run, watcher, write to media folder
2. **Categories** — four rows: on/off, preset, and the policy switches
3. **Presets** — the grouped list, the state line, Duplicate / Rename / Delete, the fields, and the
   preview
4. **Rules** — resolution ladder, minimum K, edition overrides, exclusions
5. **Backup** — export and import

### Telling the two kinds apart

An `<optgroup>` per kind in the same select:

```html
<select id="PresetId">
  <optgroup label="Built-in">   <option value="{guid}">Movie</option> …
  <optgroup label="Your presets"><option value="{guid}">Kompakt</option> …
</select>
```

`<optgroup>` is plain HTML and depends on nothing. That matters here: this page imports no module,
so `emby-input` and `emby-select` happen to be available while `emby-textarea` is not, and a layout
that *depends* on those elements is the reason the page once looked broken.

Above the fields, a line that names the state — *"Built-in — read-only"* or *"Your preset"* — and
the matching buttons.

### The preview

Portrait **and** landscape side by side, because one preset may serve both, and both states
(uniform and partial) so the colours are actually visible. Today's preview shows one poster only.

### Deleting

A preset that a category points at **cannot be deleted**; the message names the categories using
it. Preventing the dangling reference is better than handling it — but it is handled anyway: a
category pointing at an id that does not exist falls back to the built-in for that category, says
so in the log **and** on the settings page, and never quietly renders with different settings.

---

## 8. Export and import

**Presets are exported, configurations are not.** A preset is pure appearance and portable; the
configuration also holds exclusion lists and edition overrides keyed by item id, which are
meaningful on this server and nowhere else. Two separate things:

| | |
|---|---|
| **Export / import presets** | JSON, portable, for sharing and for keeping a copy before experimenting |
| **Back up the whole configuration** | JSON, labelled *this server only* |

A textarea holding the JSON with a copy button, and a paste box for import. No file dialog: a
plugin page cannot rely on one being available. A download button may be added on top.

The payload carries a **schema version**, so a later plugin can migrate or refuse cleanly instead
of guessing.

On import, in this order:

1. Validate. Report what was rejected and why; never drop a field silently.
2. Same id, identical content → keep one. Deduplicating by content beats blindly appending.
3. Same id, different content → new GUID, name gets ` (2)`.
4. Name collides with a built-in → suffix as well.

---

## 9. Migration, and why it must change nothing

The restructuring is itself a change of appearance, and there are **36 movies whose cached
"original" already carries a badge** (see the project memory note). If the look key changes, every
badged movie is redrawn — harmless for the 414 that were repaired, and a second badge for those 36.

So the upgrade does this:

- today's values become a **custom preset**, byte for byte, and the Movies category points at it.
  Not at the built-in `Movie`: the user's corner is `TopLeft` and the built-in default is not;
- Series, Seasons and Episodes are **off**;
- the look key keeps being computed from the **effective rendering values**, never from the layout
  of the configuration.

Then the key for movies is unchanged, nothing is redrawn, and the 36 stay as they are until their
metadata is cleaned up. A test should assert exactly that: the key computed from a migrated
configuration equals the key computed from the old one.

---

## 10. To verify before building

Not assumptions to act on — the two things this design leans on that have not been measured yet:

- **Can the plugin read an item's presentation key?** Series badges have to be drawn on *every* row
  of the merge group, because Jellyfin picks which row supplies the tile, and for *The Last of Us*
  it picks the 1080p one. Grouping by name is a proxy, not the real key.
- **Does SkiaSharp produce the glow the mock-up shows?** The preview stacked outlines; the renderer
  would use a blur mask filter. Similar, not the same code.

## 11. Deliberately not in scope

- Edition parsing from **file** names for episodes, which is what the 201 same-resolution
  duplicates would need. Worth doing, and cleanly separable: `SxxExx` is a hard delimiter, so
  everything after it is release zone — arguably a better anchor than the title subtraction the
  movie parser needs.
- Image types other than Primary.
- Any threshold or gauge for the partial state. Binary, by decision.
