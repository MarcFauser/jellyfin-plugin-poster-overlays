using System;

namespace Jellyfin.Plugin.PosterOverlays.Configuration;

/// <summary>
/// A look: everything about how a badge is drawn, and nothing about when it is drawn.
/// </summary>
/// <remarks>
/// Keeping those apart is what makes a preset reusable. One preset can serve movies, series and
/// seasons at once and changing it changes all three; if policy lived in here - which items are
/// badged, which badge kinds apply - the "Episode" preset could never be used for anything else.
/// <para>
/// A preset may carry settings the category using it has no use for. The completeness colours
/// mean nothing on a film, because a film is one file and is therefore always "complete". That is
/// not a contradiction: the preset <b>describes</b>, the category decides whether there is
/// anything to describe.
/// </para>
/// <para>
/// Every geometry value is a percentage, never a pixel count, so the same preset produces the same
/// look on a 600x900 master and on a 1000x1500 one. The percentages are relative to different
/// things on purpose - a pill height relative to the image height keeps the badge in proportion to
/// the poster, while a side margin relative to the width keeps it the same distance from the edge.
/// </para>
/// <para>
/// This type is persisted by <c>XmlSerializer</c>, so it has a parameterless constructor and only
/// property types that survive it. Colours are <c>#RRGGBB</c> strings rather than a colour type
/// for the same reason, with the side benefit that an exported preset is legible.
/// </para>
/// </remarks>
public class BadgePreset
{
    /// <summary>
    /// Gets or sets the stable identity a category points at.
    /// </summary>
    /// <remarks>
    /// An id and not the name, so renaming a preset cannot break an assignment, and so two
    /// configurations that both contain a "Compact" can be merged without one quietly winning.
    /// The built-in presets have fixed ids declared in <see cref="BuiltInPresets"/>.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name shown in the list. Display only - nothing resolves by it.
    /// </summary>
    public string Name { get; set; } = "Unnamed";

    /// <summary>
    /// Gets or sets the visual style of the pills.
    /// </summary>
    public BadgeStyle Style { get; set; } = BadgeStyle.DarkPill;

    /// <summary>
    /// Gets or sets the corner the badges start in.
    /// </summary>
    public BadgeCorner Corner { get; set; } = BadgeCorner.TopRight;

    /// <summary>
    /// Gets or sets which way the badges grow from that corner.
    /// </summary>
    public BadgeDirection Direction { get; set; } = BadgeDirection.Vertical;

    /// <summary>
    /// Gets or sets the pill height as a percentage of the image height.
    /// </summary>
    /// <remarks>
    /// The one value that has to differ between portrait and landscape. At a tile width of 200 px
    /// a 2:3 poster is 300 px tall and 5.5 % gives a 16 px pill; a 16:9 still at the same width is
    /// 113 px tall and the same percentage gives 6 px. The landscape preset therefore starts near
    /// 10 %, which is a starting value to be checked on a rendered still, not a law.
    /// </remarks>
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
    /// Gets or sets the gap between two pills, as a percentage of the pill height.
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
    /// Gets or sets the distance from the left or right image edge, as a percentage of the width.
    /// </summary>
    public double HorizontalMarginPercent { get; set; } = 3.0;

    /// <summary>
    /// Gets or sets the distance from the top or bottom image edge, as a percentage of the height.
    /// </summary>
    public double VerticalMarginPercent { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets how many badges may be drawn. Anything beyond this is dropped from the end of
    /// the stack, so <see cref="BadgeOrder"/> decides what survives.
    /// </summary>
    public int MaxBadges { get; set; } = 3;

    /// <summary>
    /// Gets or sets the order the badge categories are stacked in, top first, as a comma separated
    /// list of <see cref="Badges.BadgeCategory"/> names.
    /// </summary>
    public string BadgeOrder { get; set; } = "Edition,Resolution,VideoRange,Format,Source";

    /// <summary>
    /// Gets or sets a value indicating whether a badge is coloured by whether the thing it
    /// describes is available throughout.
    /// </summary>
    /// <remarks>
    /// Only meaningful where something is aggregated, so it is off in the movie and episode
    /// presets: there everything would always be "complete", and a colour that never changes is
    /// not information.
    /// </remarks>
    public bool CompletenessColours { get; set; }

    /// <summary>
    /// Gets or sets the border colour used when every child agrees, as <c>#RRGGBB</c>.
    /// </summary>
    public string UniformColour { get; set; } = "#3ED682";

    /// <summary>
    /// Gets or sets the border colour used when only some children have it, as <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// Green and amber is exactly the pair that converges under the common red-green deficiency,
    /// which is why <see cref="PartialMarker"/> stays available as a second channel even though it
    /// is nearly redundant beside the colour.
    /// </remarks>
    public string PartialColour { get; set; } = "#FFAA28";

    /// <summary>
    /// Gets or sets a value indicating whether a soft glow is drawn around the pill.
    /// </summary>
    /// <remarks>
    /// It carries no meaning of its own; it lifts the pill off busy artwork, which is worth having
    /// on a wall of posters whose backgrounds nobody controls.
    /// </remarks>
    public bool Glow { get; set; }

    /// <summary>
    /// Gets or sets the glow radius as a percentage of the pill height.
    /// </summary>
    public double GlowRadiusPercentOfPill { get; set; } = 25;

    /// <summary>
    /// Gets or sets how a partly available badge is marked apart from its colour.
    /// </summary>
    public PartialMarker PartialMarker { get; set; } = PartialMarker.Diagonal;

    /// <summary>
    /// Copies this preset under a new identity, which is the only way to edit a built-in.
    /// </summary>
    /// <param name="newId">The id of the copy.</param>
    /// <param name="newName">The name of the copy.</param>
    /// <returns>A detached copy.</returns>
    public BadgePreset CopyAs(Guid newId, string newName) => new()
    {
        Id = newId,
        Name = newName,
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
        CompletenessColours = CompletenessColours,
        UniformColour = UniformColour,
        PartialColour = PartialColour,
        Glow = Glow,
        GlowRadiusPercentOfPill = GlowRadiusPercentOfPill,
        PartialMarker = PartialMarker,
    };
}
