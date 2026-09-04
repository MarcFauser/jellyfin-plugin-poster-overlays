# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) with one deviation: the
major version encodes the Jellyfin line the build targets (`11.x` for Jellyfin 10.11, `12.x`
for Jellyfin 12), so both lines carry the same feature set under different majors.

## [Unreleased]

### Added

- **An audio-format badge — ATMOS, DTS-X, TRUEHD, DTS-HD and so on — drawn only where two copies
  of the same title differ in it.** Off by default.

  The restriction is the feature, and it comes from a measurement rather than from taste. Of 105
  groups on the reference library that share one film, 7 differ in nothing our badges could show
  except the audio format. The other 2,300-odd films have no second copy at all, and a format
  badge on those says something true about a file nobody is comparing to anything. JellyTag, which
  is where the idea came from, draws its audio badges on every poster; the difference here is the
  same principle the episode badges already follow.

  The label carries exactly as much as the job needs. The format alone settles most of it; where
  two copies are both plain DTS and differ only in the channel layout — measured on two entries of
  *Evangelion 2.0*, 5.1 against 6.1 — the channels are added. Where even that is equal, nothing is
  drawn: four groups differ only in how many tracks they carry, and "two audio tracks" is not
  something a badge can usefully say.

  Only the best track counts. A film with Atmos, a DTS-HD track and a stereo commentary is an
  Atmos film. Atmos is read from the profile and the track title as well as the codec, because
  Jellyfin reports it in the first two and never in the third — a codec-only lookup finds no Atmos
  at all.

  Grouping uses Jellyfin's own `PresentationUniqueKey` rather than an id read out of `ProviderIds`.
  That key is what decides which rows the client shows as one tile, and it follows an NFO that
  redefines the grouping; a key rebuilt from provider ids would stop agreeing with the client the
  moment somebody set a `customid`.

### Changed

- **`MergeDolbyVisionAndHdr` is spelled `MergeDolbyVisionAndHDR`.** Requested by the owner of the
  project; the house style would otherwise write a three-letter abbreviation as `Hdr`, the way
  `XmlWriter` is written, so this is a deliberate exception rather than an oversight.

  The XML element keeps the old spelling, and that is not cosmetic: renaming a property renames the
  element the serialiser looks for, so every stored configuration would have fallen back to the
  default of `true` — the opposite of what anybody who turned it off had chosen, and invisible
  until they noticed their DV and HDR badges had merged again. Two tests hold it down, and both
  were made to fail with the attribute removed before being believed.

### Fixed

- **The preview opened at the poster's stored size and covered the screen.** A `max-width: 100%`
  on the image was not a limit at all here: a percentage resolves against the container, and the
  floating panel is `position: fixed` with no width of its own, so the container grew with the
  picture and the ceiling was never reached. The panel now has a width, and the image is capped in
  both directions — a poster arrives at 1000x1500 and upwards, so one bound is not enough.

  Only 11.17.0.0 / 12.17.0.0 are affected: the preview arrived there, and in that one version it
  opened over the whole page. Anyone on an earlier version can skip straight past it.

- **Style rules applied to the whole of Jellyfin, not just this page.** The page states the rule in
  its own first comment — every selector carries the page id, because a plugin settings page is
  injected into the running client — and the rules added for the preview did not. Nothing collided,
  which is precisely why it would have gone unnoticed; a rule about `.po-hit:hover` was a rule
  about every element in the client carrying that class.

  Twelve rules in all, including four that predate the preview. There are no exceptions, though a
  first attempt at this claimed two: that the pinned panel hangs off `document.body` and so cannot
  be scoped to the page. That was reasoned rather than read. `buildFloating` ends in
  `page().appendChild(box)`, and `document.body` does not occur in the file at all — `position:
  fixed` places an element against the viewport, it does not move it out of the page.

  Both facts are now tested, the scoping check against a planted unscoped rule so it is known to
  be able to fail.

- **The pinned preview could be closed but not reopened.** The close button set `display: none`
  and nothing undid it; even reloading only worked by accident, because the panel is rebuilt only
  when it does not already exist. There is a button beside the style buttons now, it says which
  state it is in, and the choice is remembered. A control that hides something permanently and
  offers no way back is a trap, however small it looks.

### Changed

- **The preview shows the real thing instead of an imitation of it.** The settings page used to
  draw the badges itself, in SVG, onto a mock poster it also drew. That is a second implementation
  of the drawing rules, and two implementations of one thing drift apart — this pair already had.
  The centred corners had to be taught to both when they were added, and the imitation sized its
  pills from `text.length * fontSize * 0.62` where Skia measures the string properly, so no pill
  was ever quite the right width. The comment in the old code said as much: *"rough text metrics
  are enough here"*.

  Now the page posts the settings as they stand — saved or not — to a new route, the server draws
  the item through exactly the code a run uses, and the page shows what came back. A preview can no
  longer be wrong about the plugin, because it is no longer a separate opinion about it. Nothing is
  written while previewing: no upload, no state record, no cached original, and the saved
  configuration is untouched.

  Two things follow from using a real item. There is a **search box** to choose which one, and on
  opening, the page offers one the plugin already badges — picking at random would usually land on
  an untouched poster and read as a fault. And when nothing is drawn, the page now **says why**:
  the category is off, the item is on the exception list, or the episode has no second copy and
  the category only badges where that tells two apart. An unbadged preview and a broken one used
  to look identical.

  The style is picked by name now rather than by clicking one of three miniatures. Three previews
  would mean three renders per keystroke; one correct picture answers the same question. Idea
  taken from JellyTag, which previews on a real item because its middleware architecture gets it
  for free — this reaches the same place from a plugin that draws into the stored image.

- **The settings sections fold away**, and stay how they were left. The page had grown to eight
  sections of which most are set once; "Backup and sharing" and "Run it for one item" start shut.
  Done in script rather than by rewriting the markup into `<details>` elements: seven sections
  means seven pairs of tags to re-nest by hand, and a misplaced closing tag in a page this size is
  not something the build would catch.

### Added

- **A first test for the settings page.** There was none, which is how an earlier version could
  name a module that does not exist and then silently never fill in a single field — a settings
  page fails quietly, since a missing element is `null` rather than an error. The check reads every
  complete `querySelector('#x')` out of the page and insists the element is declared, and it was
  made to fail on purpose with a planted typo before being believed. It also holds the imitation
  down: if `previewSvg`, `previewRows`, `markerShape` or `allowedKindsForPreview` ever come back,
  so has the second implementation.

- **Two routes**, worth naming because they are usable from anything, not only from the settings
  page. `POST /PosterOverlays/Preview/{itemId}` takes a configuration in the body and answers with
  the image that configuration would produce — it changes nothing, neither the item nor the saved
  settings, so it is safe to call at any rate. How the picture came out is carried in headers
  rather than the body, since the body has to be the image for an `<img>` to show it:
  `X-PosterOverlays-Badges` counts them and `X-PosterOverlays-Note` says why there are none.
  `GET /PosterOverlays/PreviewCandidates` lists items the plugin already badges.

### Changed

- **The catalogue category is `MoviesAndShows` instead of `General`.** The plugin draws on the
  posters of films, series, seasons and episodes, so that is where somebody browsing the catalogue
  would look for it. `category` is not free text: the official catalogue uses exactly eight values —
  measured against `repo.jellyfin.org/files/plugin/manifest.json`, 34 packages — and a value outside
  that set parses cleanly and then belongs to no filter at all, which drops the plugin out of every
  category view.

  The value is written in two places and only one of them is ever read. `GET /Plugins` reports an
  installed plugin as `Name, Version, ConfigurationFileName, Description, Id, CanUninstall,
  HasImage, Status` — there is no category on it at all, and the dashboard groups by the repository
  manifest, visible through `GET /Packages`. So changing the manifest takes effect immediately and
  no version has to be raised for it.

  The inert copy in the shipped `meta.json` is a different matter, and a control build is what
  established it: unread or not, that copy is inside the ZIP, so changing it alters the artifact.
  The build refused itself — 11.15.0.0 is published with checksum `3d622718…` and the run produced
  `bec3a64c…`, with an identical timestamp, the category being the only difference. That refusal is
  the reason this goes out as its own version rather than as a manifest edit alone: the two copies
  are kept in step, and keeping them in step costs a release.

  Both places now read one `-Category` parameter, so they cannot be changed apart, and the parameter
  is a `ValidateSet` of the eight permitted values: a wrong one is refused before the build starts
  rather than shipping and quietly belonging to no filter. The header carried over from an existing
  manifest also has its category rewritten on every run, the way `owner` already is — without that,
  correcting the literal in the branch below would look right and do nothing, which is the trap
  `$Developer` fell into once already. Measured both ways: building with `-Category General` turned
  a manifest reading `MoviesAndShows` back into `General`, and the same run reproduced the published
  `3d622718…` byte for byte, which is what makes "the category was the only difference" a
  measurement rather than a guess.

### Added

- **Two centred positions, "Top, centred" and "Bottom, centred", for images a client crops on the
  sides.** Jellyfin's episode list shows a still in a container of its own proportions and takes
  the difference off the left and right, so a badge near either edge is cut in half — measured on
  a real episode: 2 % margin was cut, and raising it to 5 % changed nothing, because the crop is
  proportional and not a fixed number of pixels. No margin can win that argument.

  Centred sidesteps it instead of fighting it: the crop may take as much as it likes off both
  sides and a badge in the middle stays whole, so only the vertical distance still has to fit.
  The horizontal margin is deliberately ignored in these two positions — a margin is a distance
  from an edge, and there is none here; honouring it would only shift the row off centre. With a
  vertical badge direction each pill is centred on its own rather than the column as a block,
  otherwise a narrow pill under a wide one sits visibly off to one side.

  The new values are appended to the enum, so `TopRight` … `BottomLeft` keep the numbers they had
  and no existing preset changes meaning. Tests assert the drawn pixels rather than the computed
  rectangle, including a control that the ordinary corners are *not* centred and one that a 20 %
  crop per side removes a corner badge but not a centred one.

- **`Kino Fassung` written as two words is recognised as the theatrical cut.** The catalogue only
  knew the compound `kinofassung`, so a folder spelling it apart got no edition badge at all — it
  turned up in the nightly "edition-looking phrase with no rule for it" report. The pattern now
  tolerates the space (`kino ?(?:fassung|version)`), which cannot widen the match: `kino` on its
  own still fires no rule. One film in the reference library is affected.

  The other seven phrases from that report were looked at and deliberately left alone: two were
  spelling mistakes in the release name that have since been corrected at the source, and five are
  too particular to earn a catalogue entry.

### Documented

- **The repair path never downloads from an image provider that is switched off for that
  library**, and the code now says why that is not automatic. Passing the item does not restrict
  the providers by itself — "disabled" is defined relative to a library, so the item only
  supplies the frame of reference and `IncludeDisabledProviders = false` is what applies it.
  Jellyfin's own `/Items/{id}/RemoteImages` route sets that flag to `true` on purpose, because
  the picker in the web client should offer everything. This plugin downloads without asking, so
  it has to honour what was configured. No behaviour change; the guard was already there, but it
  read like tidiness somebody could drop.

### Changed

- **A folder that says two independent things now gets two edition badges.**
  `Ritter.aus.Leidenschaft.2001.REMASTERED.EXTENDED…` used to draw `EXT` alone: both rules
  matched, the first in the list won, and exactly one edition badge was ever drawn.

  The fix is not "draw everything that matches" — that would turn `Extended Directors Cut` into
  `EDC EXT DC`. Each rule now declares a **facet**: which cut this is, how the picture is framed
  or coloured, how the copy was mastered. Within a facet the first match still wins; across
  facets the badges stand side by side, in facet order, so the cut comes first and is the one
  that survives if `MaxBadges` trims the row. The cut is what tells two copies apart; the rest
  describes how a copy was made.

  Measured over 2380 real folders: exactly six carry two edition tokens, in three shapes — REM+UC
  four times, OM+UC once, EXT+REM once. All three are a cut plus something that is not a cut, and
  no pair within one facet occurs at all, which is why the rule is about meaning rather than a
  count. Those six are the only films whose badges change.

### Added

- **Episodes are read from their file name, so two copies in the same resolution can finally be
  told apart.** A movie keeps its release tags in its folder name; a flattened season is one
  folder with every episode beside each other in it, so for episodes the file name is the only
  place they live. About 201 duplicate episodes in the reference library sit in the *same*
  resolution, which makes every technical badge read identically on both — the edition token is
  the only difference there is:

  ```
  buck.rogers.s01e01.german.dl.1080p.fs.bluray.x264-excited.mkv          -> no badge
  buck.rogers.s01e01e02.german.dl.alternate.cut.1080p.bluray.x264-excited.mkv -> ALT
  ```

  The design note that proposed this called `SxxExx` a hard delimiter and treated everything after
  it as release text. That is a position rule, and the movie parser exists partly to document why
  those fail: measured against the catalogue, **twelve of twelve** plausible episode titles fire a
  rule, including *The Extended Family*, because the bare word `extended` is enough. Three
  harmless titles fired nothing, which is the control. So the anchor only marks the end of the
  series title, the episode title is subtracted exactly as a film title is, and the remaining zone
  must contain a known release tag or it is discarded whole — a stricter demand than the movie
  parser can make, and affordable only because the anchor is hard. Films are untouched: the
  non-episode path is the same call it always was, so nothing is redrawn.

- **Records whose item no longer exists are cleared at the end of the nightly run.** Until now
  every record and cached original stayed on disk forever, and only the task that undoes the whole
  plugin ever collected them. That is more than tidiness: shedding a stale provider id requires
  the entry to be *recreated*, which mints a new id, so ordinary metadata repair produces orphans.

  Not an `ItemRemoved` subscription, and not for cost reasons — **the event does not report what
  would need reporting.** In `LibraryManager.DeleteItem` on `release-10.11.z` the recursive
  children are removed from the repository in one call and dropped from the cache in a loop, and
  then the event is raised *once, for the root item only*. Delete a series and exactly one
  notification arrives while its episodes vanish silently — and episodes are precisely the records
  at stake. An event also only helps while something is listening: a deletion during an upgrade or
  a crash between removal and write leaves an orphan no later event will ever mention.

  A reconciliation rather than a smarter key, deliberately. A GUID dies when an entry is
  recreated, a path dies on rename, and a virtual entry never had one — every key has a lifetime,
  and a comparison against the library does not care which one expired. The sweep refuses to act
  when the library looks wrong rather than smaller (nothing found at all, or more than half the
  records unmatched) because the asymmetry is severe: a kept dead record wastes half a megabyte,
  while a wrongly dropped one destroys the only unbadged copy of that cover and the next run then
  draws a badge on top of a badge. A record whose key is not an item id is reported and never
  removed.

### Fixed

- **The two colour swatches showed black and a colour picked in them had no effect.** Not the
  picker: the presets themselves held empty strings, written by the load-time clobber fixed in the
  release before this one. An `<input type="color">` with no value is `#000000`, so an absence
  looked like a choice. Blank colours are now filled in with the default when the configuration is
  loaded and when the page shows a preset, and the swatch never renders a colour that is not the
  one in use. Repairing the stored value is free rather than destructive because everything that
  reads a colour - the look key included - goes through one accessor that treats blank as the
  default, so blank and `#3ED682` already produced the same key. A test pins that, and with the
  key reverted to the raw property it fails; nothing is redrawn by the repair, which matters for
  the items whose cached original already carries a badge.
- **Ticking "Color the badge by whether it holds throughout" appeared to show the picture twice.**
  It was showing both halves of that setting - everything agrees, and only some do - with nothing
  to say which was which. Both rows are captioned now. The difference between them is the entire
  point of the setting, so it is spelled out rather than left to be inferred.
- **The step buttons are smaller and centred**, and centred by flex rather than by line height:
  `+` and `−` do not have the same ink height, and a box that is centred is not the same as ink
  that is centred.
- **The settings page overwrote the selected preset while it was still loading.** Filling a field
  dispatches a change event, the event bubbles to the form, and the form handler writes the fields
  back into the preset - so during page setup, with the preset fields still empty, it wrote blanks
  over a perfectly good preset. That is how one ended up named "Unnamed" carrying whatever the
  selects happened to default to. Nothing is written back now while the page is being populated.
- **The spinner spacing, third attempt and this time not a guess.** The first tried
  `padding-right`, which moves the arrows along with the digits; the second a margin on
  `::-webkit-inner-spin-button`, which did not take. Both were guesses about a shadow tree this
  page does not own. The native control is now switched off and replaced by two ordinary buttons
  in the same flex row as everything else, so the gap is set here and stays set.

### Added

- **A floating preview**, pinned to the right edge, showing the selected style as it is edited.
  Every field on this page changes a picture, and a picture you have to scroll back to is one you
  stop looking at. It can be dismissed, and it hides itself on narrow windows where it would sit
  on top of the fields.

### Fixed

- **Saving the settings page threw the presets away.** A configuration travels through two
  serialisers: `XmlSerializer` writes the file, and `System.Text.Json` carries it to and from the
  settings page. The first populates a read-only collection property, the second does not - and it
  says nothing about it. So the presets were written to disk correctly, shown on the page
  correctly, and dropped the moment Save was pressed, leaving every category pointing at a preset
  that no longer existed. The property now carries
  `[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]`.
  The XML round trip had a test from the first day; the JSON one did not, because the file format
  was checked and the transport was assumed. It has one now, and with the attribute removed it
  fails while the XML test still passes - which is exactly the picture that let this through.
- **A configuration already emptied by that fault repairs itself.** The legacy flat settings were
  kept rather than deleted, so they still hold what the lost preset held; the preset is rebuilt
  under the very id the category is already pointing at. The look key therefore comes out
  identical and nothing is redrawn - which matters, because a redraw starts from the cached
  original and 30 of those already carry a badge. The repair is narrow on purpose: it only fires
  when there are no custom presets at all and the reference is dangling.
- **The spinner arrows now really have room.** The previous attempt added `padding-right`, which
  was the wrong side: the spin button sits at the inline end of the field, so padding pushes the
  arrows inwards along with the digits and the gap lands beyond them. The space belongs between
  the two, which means a margin on the spin button itself.
- A preset saved without a name becomes "Unnamed" rather than an invisible entry in the list.

### Added

- **A colour picker beside each of the two completeness colours**, with the swatch showing the
  current value. The text field stays the thing that is saved: it can hold something the picker
  cannot represent, and quietly rewriting what somebody typed is worse than a swatch that has not
  caught up.

### Added

- **Series, seasons and episodes are badged.** The three categories are no longer settings without
  an effect; the nightly task collects whichever kinds are switched on, and a library where only
  films are badged is not walked over 25,000 episodes to say "skipped".
- **What a series may claim is aggregated from its episodes, and duplicates are collapsed first.**
  There are two ways for episodes to disagree and only one of them means the series is mixed. If
  the same episode exists twice - once 1080p, once 4K - nothing is missing and the series *is*
  available in 4K; if different episodes differ, it genuinely is not. Collapsing every episode to
  what its best copy offers before requiring agreement is what separates the two, and without it
  the first case reads as mixed. Measured on a real library: one show has all 28 episodes in both
  1080p DV and 4K DV, and calling it mixed would be false. Two tests pin it, and they were made to
  fail by switching the collapse off, because a pair of tests that pass either way proves nothing.
- The state is binary, as decided: 7 of 8 and 22 of 52 are both simply "not uniform". A series
  whose episodes carry nothing notable gets **no badge at all**, not a partial one - the partial
  state only appears where something worth showing actually varies. Where a show spans two rungs
  the higher one is shown and marked partial, because "8K is available, but not throughout" is the
  useful statement and 4K would understate it.
- The episodes are fetched by **presentation key**, not by parent id: a show split across
  resolution folders is several database rows the client merges into one tile - one show here is
  three Series rows and six Season rows - and the badge has to describe the tile. Rows without a
  file are dropped, or the thousands of missing-episode placeholders would make almost every
  series partial for episodes that were never there.
- **Episodes are badged only where it tells two copies apart**, by default. On a poster wall a
  badge helps you choose; in an episode list you already know which episode you want and the only
  question is which copy. The twin lookup is answered once per series and remembered - the
  alternative is one query per episode, which is 25,419 of them against roughly 1,580 this way.
- The availability is part of the badge key, so a series that gains its last missing 4K episode is
  redrawn even though the label did not change - it changes colour.

- **Presets.** How a badge looks is now a named preset, and each kind of entry - movies, series,
  seasons, episodes - picks one. A preset is a *look* and a category is a *policy*, which is what
  lets one preset serve several categories at once and keeps changing the style a single edit
  rather than four. Four presets ship with the plugin and are **read-only**: the fields are
  disabled while one is selected and a Duplicate button makes a copy that is not. Built-in and
  custom presets are two `optgroup`s in one list, which is plain HTML and therefore does not
  depend on the `emby-*` elements a plugin page cannot rely on.
- Presets are referenced by id, never by name, so renaming one cannot break an assignment and two
  configurations that both contain a "Compact" can be merged without one quietly winning. The
  built-in ids are counted from one (`...0001` to `...0004`) rather than random - they are never
  generated, so collision is not a risk, and a category pointing at `...0002` is visibly pointing
  at something shipped. One is the first number on purpose: all zeroes is `Guid.Empty`, which is
  what an unset id holds, and that has to keep falling back loudly instead of resolving to a
  built-in nobody chose.
- **Export and import of presets**, as JSON with a schema version, through a box on the settings
  page rather than a file dialog a plugin page cannot rely on. Presets only: the rest of the
  configuration is keyed by item id and means nothing on another server. An import never
  overwrites - identical content is recognised and skipped, a taken id arrives as a copy with a
  numbered name, and a newer schema is refused rather than guessed at.
- **Support in the renderer for showing that a badge only holds for part of what is underneath**:
  a colour for uniform and one for partial, an optional glow, and a marker - split vertically, on
  a slant, wavy, or hatched. Every option keeps a filled background under the whole label; a
  half-empty pill loses its text over the empty half on a bright poster, which is the one thing
  the pill exists to prevent. Only series and seasons can be in that state, so the movie and
  episode presets have it off, and settings that cannot reach the pixels stay out of the look key.
- A preview that shows portrait **and** landscape, because one preset can serve both, and both
  states where the traffic light is on.
- The glow and the partial marker are covered by render tests against the real renderer. That is
  not routine coverage: the mock-ups that sold both ideas were drawn in a different library
  altogether, so until now "SkiaSharp produces this" was a plan and not a measurement. The glow
  test looks for the halo in a band strictly above the pill and has a no-glow control, because
  otherwise "these pixels differ" would pass for a halo; the marker test sets both colours alike
  so the border cannot account for the difference.
- `GET /PosterOverlays/BuiltInPresets`, so the settings page does not carry a second copy of the
  built-in table that would drift from the one in code.

### Changed

- The configuration migrates itself once, on load, and **nothing changes**: the old flat values
  become a custom preset byte for byte and the movie category points at it - deliberately not at
  the built-in, whose defaults are not necessarily what was configured. The other three categories
  stay off. There is a test asserting the movie look key is unchanged, and it was made to fail on
  purpose to prove it can: without it an upgrade would redraw every badged movie, and a redraw
  starts from the cached original, of which 31 already carry a badge from the faulty first release.
- The old flat settings are kept in the configuration class rather than deleted. `XmlSerializer`
  ignores elements it does not know, so removing a property does not fail - it silently loses
  whatever was stored. They can go one release after every configuration has been migrated.
- Series, season and episode categories are visible on the settings page and their settings are
  kept, but nothing draws them yet; the rows say so rather than offering a switch that does
  nothing.

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
