using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.PosterOverlays;
using Jellyfin.Plugin.PosterOverlays.Configuration;
using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Presets, the categories that point at them, and the one-time migration.
/// </summary>
/// <remarks>
/// Two of these guard hazards rather than features, and both are silent when they go wrong.
/// <list type="bullet">
/// <item>
/// Jellyfin persists a plugin configuration with <c>XmlSerializer</c>, and its load path catches
/// every exception, writes the defaults back and says nothing. A property type the serialiser
/// cannot handle therefore destroys the user's settings without a message.
/// </item>
/// <item>
/// If the migration changes the look key by so much as a character, every already badged movie is
/// redrawn - and a redraw starts from the cached original, of which some already carry a badge
/// from the faulty first release.
/// </item>
/// </list>
/// </remarks>
public class PresetsAndMigrationTests
{
    /// <summary>
    /// The exact key the flat configuration produced before presets existed, spelled out rather
    /// than computed, so that a change to the format has to be made here on purpose.
    /// </summary>
    private static string LegacyKey(PluginConfiguration c)
    {
        var i = CultureInfo.InvariantCulture;
        return string.Join(
            '|',
            c.Style.ToString(),
            c.Corner.ToString(),
            c.Direction.ToString(),
            c.PillHeightPercent.ToString("R", i),
            c.FontSizePercentOfPill.ToString("R", i),
            c.PaddingPercentOfPill.ToString("R", i),
            c.GapPercentOfPill.ToString("R", i),
            c.CornerRadiusPercentOfPill.ToString("R", i),
            c.BorderWidthPercentOfPill.ToString("R", i),
            c.HorizontalMarginPercent.ToString("R", i),
            c.VerticalMarginPercent.ToString("R", i),
            c.JpegQuality.ToString(i));
    }

    /// <summary>
    /// Settings that are not the defaults, because a migration that only works on defaults proves
    /// nothing - the user whose library this was built against runs top left and horizontal.
    /// </summary>
    private static PluginConfiguration OldConfiguration() => new()
    {
        Corner = BadgeCorner.TopLeft,
        Direction = BadgeDirection.Horizontal,
        Style = BadgeStyle.DarkPill,
        PillHeightPercent = 5.5,
        FontSizePercentOfPill = 60,
        PaddingPercentOfPill = 42,
        GapPercentOfPill = 33,
        CornerRadiusPercentOfPill = 20,
        BorderWidthPercentOfPill = 3.5,
        HorizontalMarginPercent = 3.0,
        VerticalMarginPercent = 2.0,
        MaxBadges = 3,
        BadgeOrder = "Edition,Resolution,VideoRange,Format,Source",
        JpegQuality = 95,
        ShowSourceBadges = false,
    };

    [Fact]
    public void AFreshConfigurationIsAlreadyOnTheCurrentLayout()
    {
        // A new installation has nothing to migrate, and must not acquire a "Migrated" preset it
        // never needed.
        var config = new PluginConfiguration { SettingsVersion = 2 };

        Assert.False(config.Migrate());
        Assert.Empty(config.CustomPresets);
    }

    [Fact]
    public void MigrationCarriesTheOldValuesIntoACustomPreset()
    {
        var config = OldConfiguration();

        Assert.True(config.Migrate());

        var preset = Assert.Single(config.CustomPresets);
        Assert.Equal(BadgeCorner.TopLeft, preset.Corner);
        Assert.Equal(BadgeDirection.Horizontal, preset.Direction);
        Assert.Equal(5.5, preset.PillHeightPercent);
        Assert.Equal(preset.Id, config.Movies.PresetId);
        Assert.True(config.Movies.Enabled);

        // Deliberately not the built-in: its defaults are not necessarily what the user had.
        Assert.False(BuiltInPresets.IsBuiltIn(config.Movies.PresetId));
    }

    /// <summary>
    /// The one that matters. If this fails, upgrading redraws the whole library.
    /// </summary>
    [Fact]
    public void MigrationLeavesTheMovieLookKeyByteIdentical()
    {
        var config = OldConfiguration();
        string before = LegacyKey(config);

        config.Migrate();
        string after = OverlayApplier.LookKeyOf(config.PresetFor(BadgeTarget.Movie), config.JpegQuality);

        Assert.Equal(before, after);
    }

    [Fact]
    public void MigrationDoesNotRunTwice()
    {
        var config = OldConfiguration();

        Assert.True(config.Migrate());
        Assert.False(config.Migrate());
        Assert.Single(config.CustomPresets);
    }

    [Fact]
    public void MigrationCarriesTheBadgeKindsIntoTheCategory()
    {
        var config = OldConfiguration();
        config.Migrate();

        Assert.True(config.Movies.AllowEdition);
        Assert.False(config.Movies.AllowSource);
    }

    [Fact]
    public void MigrationLeavesTheOtherCategoriesOff()
    {
        var config = OldConfiguration();
        config.Migrate();

        Assert.False(config.Series.Enabled);
        Assert.False(config.Seasons.Enabled);
        Assert.False(config.Episodes.Enabled);
    }

    /// <summary>
    /// Round-tripped through the real serialiser Jellyfin uses, not a hand-rolled one: the whole
    /// risk is a type this particular serialiser cannot carry.
    /// </summary>
    [Fact]
    public void TheConfigurationSurvivesTheXmlSerializer()
    {
        var config = OldConfiguration();
        config.Migrate();
        config.CustomPresets.Add(new BadgePreset
        {
            Id = Guid.NewGuid(),
            Name = "Landscape",
            PillHeightPercent = 10,
            CompletenessColours = true,
            PartialMarker = PartialMarker.Wave,
            Glow = true,
            UniformColour = "#112233",
        });
        config.Episodes.Enabled = true;
        config.Episodes.PresetId = config.CustomPresets[1].Id;

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var buffer = new StringWriter(CultureInfo.InvariantCulture);
        serializer.Serialize(buffer, config);
        string xml = buffer.ToString();

        var back = (PluginConfiguration)serializer.Deserialize(new StringReader(xml))!;

        Assert.Equal(2, back.CustomPresets.Count);
        Assert.Equal("Landscape", back.CustomPresets[1].Name);
        Assert.Equal(PartialMarker.Wave, back.CustomPresets[1].PartialMarker);
        Assert.Equal("#112233", back.CustomPresets[1].UniformColour);
        Assert.Equal(10, back.CustomPresets[1].PillHeightPercent);
        Assert.True(back.Episodes.Enabled);
        Assert.Equal(config.Episodes.PresetId, back.Episodes.PresetId);
        Assert.Equal(2, back.SettingsVersion);

        // And the numbers are written invariant, or a server under de-DE would write "5,5" and a
        // server under en-US would read it as fifty-five.
        Assert.Contains("<PillHeightPercent>5.5</PillHeightPercent>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The round trip the settings page makes, which is <b>not</b> the one the file makes.
    /// </summary>
    /// <remarks>
    /// This test exists because its absence cost a user their configuration. A plugin
    /// configuration travels through two serialisers: <c>XmlSerializer</c> writes the file, and
    /// <c>System.Text.Json</c> carries it to and from the settings page. The first populates a
    /// read-only collection property and the second does not - silently, with no error anywhere.
    /// So the presets were written to disk correctly, shown on the page correctly, and dropped the
    /// moment Save was pressed. The XML round trip was tested; the JSON one was assumed.
    /// </remarks>
    [Fact]
    public void TheConfigurationSurvivesTheJsonRoundTripTheSettingsPageMakes()
    {
        var config = OldConfiguration();
        config.Migrate();
        config.CustomPresets.Add(new BadgePreset { Id = Guid.NewGuid(), Name = "Mine", PillHeightPercent = 9 });

        string json = System.Text.Json.JsonSerializer.Serialize(config);
        var back = System.Text.Json.JsonSerializer.Deserialize<PluginConfiguration>(json)!;

        Assert.Equal(2, back.CustomPresets.Count);
        Assert.Equal("Mine", back.CustomPresets[1].Name);
        Assert.Equal(9, back.CustomPresets[1].PillHeightPercent);
        Assert.Equal(config.Movies.PresetId, back.Movies.PresetId);

        // And the reference still resolves, which is the thing that actually broke: an empty
        // collection leaves every category pointing at a preset that no longer exists.
        Assert.True(back.PresetReferenceIsIntact(BadgeTarget.Movie));
    }

    /// <summary>
    /// The repair for the configurations that were already emptied by the fault above.
    /// </summary>
    [Fact]
    public void APresetLostInTransitIsRebuiltFromTheLegacySettings()
    {
        // What the server looked like afterwards: version 1, no presets, a dangling reference,
        // and the legacy fields still carrying what the lost preset had held.
        var wrecked = OldConfiguration();
        wrecked.SettingsVersion = 1;
        wrecked.Movies = new CategorySettings { Enabled = true, PresetId = Guid.NewGuid() };

        Assert.False(wrecked.PresetReferenceIsIntact(BadgeTarget.Movie));

        Assert.True(wrecked.Migrate());

        var preset = Assert.Single(wrecked.CustomPresets);
        Assert.Equal(wrecked.Movies.PresetId, preset.Id);
        Assert.Equal(BadgeCorner.TopLeft, preset.Corner);
        Assert.Equal(BadgeDirection.Horizontal, preset.Direction);
        Assert.True(wrecked.PresetReferenceIsIntact(BadgeTarget.Movie));

        // Rebuilt under the id the category already points at, so the look key is what it was and
        // nothing is redrawn - which is the whole point, because a redraw would put a second badge
        // on every item whose cached original already carries one.
        Assert.Equal(LegacyKey(wrecked), OverlayApplier.LookKeyOf(preset, wrecked.JpegQuality));
    }

    /// <summary>
    /// And it must not touch a configuration that is merely different, only one that is broken.
    /// </summary>
    [Fact]
    public void TheRepairLeavesAHealthyConfigurationAlone()
    {
        var healthy = new PluginConfiguration { SettingsVersion = 1 };
        healthy.CustomPresets.Add(new BadgePreset { Id = Guid.NewGuid(), Name = "Mine" });
        healthy.Movies.PresetId = healthy.CustomPresets[0].Id;

        healthy.Migrate();

        Assert.Single(healthy.CustomPresets);
        Assert.Equal("Mine", healthy.CustomPresets[0].Name);
        Assert.Equal(2, healthy.SettingsVersion);
    }

    [Fact]
    public void ABuiltInCannotBeChangedThroughWhatTheAccessorHandsOut()
    {
        var first = BuiltInPresets.Get(BuiltInPresets.MovieId)!;
        first.PillHeightPercent = 99;
        first.Name = "vandalised";

        var second = BuiltInPresets.Get(BuiltInPresets.MovieId)!;

        Assert.Equal(5.5, second.PillHeightPercent);
        Assert.Equal("Movie", second.Name);
    }

    [Fact]
    public void TheBuiltInIdsAreDistinctAndRecognised()
    {
        var ids = BuiltInPresets.All().Select(p => p.Id).ToList();

        Assert.Equal(4, ids.Count);
        Assert.Equal(4, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(BuiltInPresets.IsBuiltIn(id)));
        Assert.False(BuiltInPresets.IsBuiltIn(Guid.NewGuid()));
    }

    /// <summary>
    /// The built-in ids are counted from one, and the reason is this assertion. An unset
    /// <c>PresetId</c> holds <see cref="Guid.Empty"/>; if that resolved to a built-in, a category
    /// nobody configured would quietly draw with somebody's defaults instead of reporting that it
    /// has no preset.
    /// </summary>
    [Fact]
    public void AnUnsetPresetIdIsNotABuiltIn()
    {
        Assert.False(BuiltInPresets.IsBuiltIn(Guid.Empty));
        Assert.Null(BuiltInPresets.Get(Guid.Empty));

        var config = new PluginConfiguration { SettingsVersion = 2 };
        config.Series.PresetId = Guid.Empty;

        Assert.False(config.PresetReferenceIsIntact(BadgeTarget.Series));
    }

    /// <summary>
    /// A category pointing at a preset that no longer exists must fall back to the built-in for
    /// that category, and the caller must be able to tell that it happened.
    /// </summary>
    [Fact]
    public void ADanglingPresetFallsBackToTheBuiltInAndSaysSo()
    {
        var config = new PluginConfiguration { SettingsVersion = 2 };
        config.Episodes.PresetId = Guid.NewGuid();

        Assert.False(config.PresetReferenceIsIntact(BadgeTarget.Episode));
        Assert.Equal(BuiltInPresets.EpisodeId, config.PresetFor(BadgeTarget.Episode).Id);

        // Not "whatever preset happens to exist": drawing with settings nobody chose is the
        // failure that looks like success.
        Assert.Equal(10.0, config.PresetFor(BadgeTarget.Episode).PillHeightPercent);
    }

    [Fact]
    public void EveryCategoryResolvesToItsOwnSettings()
    {
        var config = new PluginConfiguration { SettingsVersion = 2 };

        Assert.Same(config.Movies, config.CategoryFor(BadgeTarget.Movie));
        Assert.Same(config.Series, config.CategoryFor(BadgeTarget.Series));
        Assert.Same(config.Seasons, config.CategoryFor(BadgeTarget.Season));
        Assert.Same(config.Episodes, config.CategoryFor(BadgeTarget.Episode));
    }

    [Fact]
    public void CopyingAPresetProducesADetachedTwin()
    {
        var original = BuiltInPresets.Get(BuiltInPresets.SeriesId)!;
        var copy = original.CopyAs(Guid.NewGuid(), "Mine");

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Mine", copy.Name);
        Assert.Equal(original.CompletenessColours, copy.CompletenessColours);
        Assert.Equal(original.PartialMarker, copy.PartialMarker);

        copy.PillHeightPercent = 42;
        Assert.NotEqual(42, original.PillHeightPercent);
    }
}
