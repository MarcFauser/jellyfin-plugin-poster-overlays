using System;
using System.Collections.ObjectModel;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// Settings of the poster overlay plugin.
/// </summary>
/// <remarks>
/// Every geometry value is a percentage, never a pixel count: the same code has to produce the
/// same look on a 600x900 master and on a 1000x1500 one. The defaults are the values that were
/// chosen on rendered comparisons against a real poster at real card size, not guessed.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task does anything at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the plugin only reports what it would do.
    /// </summary>
    /// <remarks>
    /// Nothing is uploaded, no file is written and no state is recorded, so a dry run leaves the
    /// library exactly as it was and can be repeated. It is the honest way to find out what a
    /// first run would change before it changes it.
    /// </remarks>
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an item is re-badged as soon as Jellyfin reports
    /// that its image changed.
    /// </summary>
    /// <remarks>
    /// This is what makes "Refresh metadata" in the item menu work as a manual trigger: the
    /// provider delivers a new cover, the plugin notices and puts the badge back. It also means
    /// the plugin writes during every library refresh, which is why it can be turned off.
    /// </remarks>
    public bool WatchForImageChanges { get; set; } = true;

    // ---------------------------------------------------------------------------------------
    // Legacy, from before presets existed. Nothing reads these except Migrate(), which copies
    // them into a custom preset once and then sets SettingsVersion. They are kept rather than
    // deleted because XmlSerializer silently ignores elements it does not know: removing a
    // property does not fail, it just loses whatever the user had set. They can go one release
    // after every configuration in the wild has been migrated.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the corner the badge stack starts in. The stack always grows downwards.
    /// </summary>
    public BadgeCorner Corner { get; set; } = BadgeCorner.TopRight;

    /// <summary>
    /// Gets or sets which way the badges grow from the corner.
    /// </summary>
    public BadgeDirection Direction { get; set; } = BadgeDirection.Vertical;

    /// <summary>
    /// Gets or sets the visual style of the pills.
    /// </summary>
    public BadgeStyle Style { get; set; } = BadgeStyle.DarkPill;

    /// <summary>
    /// Gets or sets how many badges may be drawn on one image. Anything beyond this is dropped
    /// from the end of the stack, so the order below decides what survives.
    /// </summary>
    public int MaxBadges { get; set; } = 3;

    /// <summary>
    /// Gets or sets the order the badge categories are stacked in, top first, as a comma
    /// separated list of <see cref="Badges.BadgeCategory"/> names.
    /// </summary>
    /// <remarks>
    /// This is also the priority: when there are more badges than <see cref="MaxBadges"/>, the
    /// ones at the end are dropped. Edition comes first by default because it is the reason the
    /// plugin exists - two folders of one film differ in the cut, not in the resolution.
    /// Unknown names are ignored, and any category missing from the list is appended in its
    /// natural order rather than silently dropped.
    /// </remarks>
    public string BadgeOrder { get; set; } = "Edition,Resolution,VideoRange,Format,Source";

    /// <summary>
    /// Gets or sets the pill height as a percentage of the image height.
    /// </summary>
    public double PillHeightPercent { get; set; } = 5.5;

    /// <summary>
    /// Gets or sets the font size as a percentage of the pill height.
    /// </summary>
    public double FontSizePercentOfPill { get; set; } = 60;

    /// <summary>
    /// Gets or sets the horizontal padding inside a pill, as a percentage of the pill height.
    /// </summary>
    public double PaddingPercentOfPill { get; set; } = 42;

    /// <summary>
    /// Gets or sets the vertical gap between two pills, as a percentage of the pill height.
    /// </summary>
    public double GapPercentOfPill { get; set; } = 33;

    /// <summary>
    /// Gets or sets the corner radius of a pill, as a percentage of the pill height. 50 gives
    /// fully rounded ends.
    /// </summary>
    public double CornerRadiusPercentOfPill { get; set; } = 20;

    /// <summary>
    /// Gets or sets the border width of a pill, as a percentage of the pill height.
    /// </summary>
    public double BorderWidthPercentOfPill { get; set; } = 3.5;

    /// <summary>
    /// Gets or sets the distance from the left or right image edge, as a percentage of the
    /// image width.
    /// </summary>
    public double HorizontalMarginPercent { get; set; } = 3.0;

    /// <summary>
    /// Gets or sets the distance from the top or bottom image edge, as a percentage of the
    /// image height.
    /// </summary>
    public double VerticalMarginPercent { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets a value indicating whether edition badges (EXT, DC, UC, ...) are drawn.
    /// They are parsed from the folder name.
    /// </summary>
    public bool ShowEditionBadges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether resolution badges (4K, 8K, ...) are drawn.
    /// </summary>
    public bool ShowResolutionBadges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether video range badges (DV, HDR, ...) are drawn.
    /// </summary>
    public bool ShowVideoRangeBadges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether presentation-format badges (3D) are drawn.
    /// </summary>
    public bool ShowFormatBadges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether source-quality badges (CAM, TS, TC, SCR) are
    /// drawn. These mark a placeholder rip and are meant to be noticed.
    /// </summary>
    public bool ShowSourceBadges { get; set; } = true;

    // --------------------------- end of the legacy block -----------------------------------

    /// <summary>
    /// Gets or sets a value indicating whether Dolby Vision and HDR are merged into one pill
    /// reading "DV HDR" instead of occupying two rows.
    /// </summary>
    public bool MergeDolbyVisionAndHdr { get; set; } = true;

    /// <summary>
    /// Gets or sets the resolution ladder, as a comma separated list of the "K" numbers that
    /// may appear on a badge. A measured pixel width is converted with width / 960 and then
    /// snapped to the nearest entry.
    /// </summary>
    /// <remarks>
    /// Only 4K and 8K have standardised widths; everything above is extrapolation, which is
    /// exactly why this is a setting and not a compiled-in table.
    /// </remarks>
    public string ResolutionLadder { get; set; } = "4,5,6,8,10,12,16,24,32";

    /// <summary>
    /// Gets or sets the lowest ladder entry that still earns a badge. With the default of 4,
    /// 1080p material gets none.
    /// </summary>
    public int MinimumResolutionK { get; set; } = 4;

    /// <summary>
    /// Gets or sets the JPEG quality used when a badged image is written back. Jellyfin
    /// delivers at 90; going below that would make the badged copy visibly worse than the
    /// original.
    /// </summary>
    public int JpegQuality { get; set; } = 95;

    /// <summary>
    /// Gets or sets a value indicating whether the badged image is written into the media
    /// folder as a local image file instead of being uploaded through Jellyfin.
    /// </summary>
    /// <remarks>
    /// Off by default and deliberately so: a local file is not overwritten when a provider
    /// delivers a new cover, which defeats the whole point of the upkeep loop. It exists for
    /// installations that want the durable variant.
    /// </remarks>
    public bool WriteToMediaFolder { get; set; } = false;

    /// <summary>
    /// Gets or sets the item ids that are never touched, one per line.
    /// </summary>
    public string ExcludedItemIds { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets manual edition overrides, one per line, as "itemId = BADGE". An empty
    /// badge suppresses the edition badge for that item without excluding it entirely.
    /// </summary>
    public string EditionOverrides { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the layout version of this configuration.
    /// </summary>
    /// <remarks>
    /// A configuration written before presets existed has no such element, and
    /// <c>XmlSerializer</c> leaves an absent element at the type default - measured, not assumed.
    /// Zero therefore means "not migrated yet" without needing a flag anybody had to remember to
    /// write.
    /// </remarks>
    public int SettingsVersion { get; set; }

    /// <summary>
    /// Gets the presets the user has made. The built-ins are not in here; they live in code.
    /// </summary>
    /// <remarks>
    /// Read-only property over a mutable collection, which is what <c>XmlSerializer</c> wants and
    /// what the analysers want. Verified to round-trip before the design was built on it.
    /// </remarks>
    public Collection<BadgePreset> CustomPresets { get; } = [];

    /// <summary>
    /// Gets or sets the policy for films.
    /// </summary>
    public CategorySettings Movies { get; set; } = new() { Enabled = true, PresetId = BuiltInPresets.MovieId };

    /// <summary>
    /// Gets or sets the policy for series.
    /// </summary>
    public CategorySettings Series { get; set; } = new() { PresetId = BuiltInPresets.SeriesId, AllowEdition = false, AllowSource = false };

    /// <summary>
    /// Gets or sets the policy for seasons.
    /// </summary>
    public CategorySettings Seasons { get; set; } = new() { PresetId = BuiltInPresets.SeasonId, AllowEdition = false, AllowSource = false };

    /// <summary>
    /// Gets or sets the policy for episodes.
    /// </summary>
    public CategorySettings Episodes { get; set; } = new() { PresetId = BuiltInPresets.EpisodeId, AllowSource = false };

    /// <summary>
    /// Returns the policy for a kind of item.
    /// </summary>
    /// <param name="target">The kind.</param>
    /// <returns>Its settings.</returns>
    public CategorySettings CategoryFor(BadgeTarget target) => target switch
    {
        BadgeTarget.Series => Series,
        BadgeTarget.Season => Seasons,
        BadgeTarget.Episode => Episodes,
        _ => Movies,
    };

    /// <summary>
    /// Finds a preset by id, built-in or custom.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <returns>The preset, or null when nothing has that id.</returns>
    public BadgePreset? FindPreset(Guid id)
    {
        var builtIn = BuiltInPresets.Get(id);
        if (builtIn is not null)
        {
            return builtIn;
        }

        foreach (var preset in CustomPresets)
        {
            if (preset.Id == id)
            {
                return preset;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the look a kind of item is drawn with.
    /// </summary>
    /// <remarks>
    /// A dangling reference - the preset was deleted, or this configuration was imported from
    /// somewhere else - falls back to the built-in for that category. It never falls back to
    /// "some other preset that happens to exist", because drawing with settings nobody chose is
    /// the failure mode that looks like success. The caller is expected to report the fallback;
    /// deleting a preset that is in use is refused in the first place.
    /// </remarks>
    /// <param name="target">The kind of item.</param>
    /// <returns>The preset, never null.</returns>
    public BadgePreset PresetFor(BadgeTarget target)
    {
        var found = FindPreset(CategoryFor(target).PresetId);
        return found ?? BuiltInPresets.Get(BuiltInPresets.DefaultFor(target))!;
    }

    /// <summary>
    /// Says whether the preset a category points at actually exists.
    /// </summary>
    /// <param name="target">The kind of item.</param>
    /// <returns>False when the reference dangles and a fallback is in use.</returns>
    public bool PresetReferenceIsIntact(BadgeTarget target) =>
        FindPreset(CategoryFor(target).PresetId) is not null;

    /// <summary>
    /// Brings a configuration written before presets existed up to the current layout, once.
    /// </summary>
    /// <remarks>
    /// <b>The whole point is that nothing changes.</b> The old flat values become a custom preset
    /// byte for byte and the movie category points at it - deliberately not at the built-in
    /// <c>Movie</c>, whose defaults are not necessarily what the user had. The other three
    /// categories stay switched off. Because the look key is computed from the effective values
    /// and never from the layout of the configuration, the key for every already badged movie is
    /// unchanged, so nothing is redrawn.
    /// <para>
    /// That matters beyond tidiness: a redraw starts from the cached original, and there are items
    /// whose cached original already carries a badge from the faulty first release. For those, a
    /// redraw means a second badge.
    /// </para>
    /// </remarks>
    /// <returns>True when something was migrated and the configuration should be saved.</returns>
    public bool Migrate()
    {
        if (SettingsVersion >= 1)
        {
            return false;
        }

        var carried = new BadgePreset
        {
            Id = Guid.NewGuid(),
            Name = "Migrated",
            Style = Style,
            Corner = Corner,
            Direction = Direction,
            PillHeightPercent = PillHeightPercent,
            FontSizePercentOfPill = FontSizePercentOfPill,
            PaddingPercentOfPill = PaddingPercentOfPill,
            GapPercentOfPill = GapPercentOfPill,
            CornerRadiusPercentOfPill = CornerRadiusPercentOfPill,
            BorderWidthPercentOfPill = BorderWidthPercentOfPill,
            HorizontalMarginPercent = HorizontalMarginPercent,
            VerticalMarginPercent = VerticalMarginPercent,
            MaxBadges = MaxBadges,
            BadgeOrder = BadgeOrder,
        };

        CustomPresets.Add(carried);

        Movies = new CategorySettings
        {
            Enabled = true,
            PresetId = carried.Id,
            AllowEdition = ShowEditionBadges,
            AllowResolution = ShowResolutionBadges,
            AllowVideoRange = ShowVideoRangeBadges,
            AllowFormat = ShowFormatBadges,
            AllowSource = ShowSourceBadges,
        };

        SettingsVersion = 1;
        return true;
    }
}
