namespace Jellyfin.Plugin.PosterOverlays.Badges;

/// <summary>
/// The three fields of an audio stream that say what format it is, plus its channel count.
/// </summary>
/// <remarks>
/// A plain record rather than Jellyfin's <c>MediaStream</c> so the classification stays testable
/// without a library: the rest of <see cref="TechnicalBadges"/> is written the same way, and the
/// tests for it construct inputs by hand.
/// <para>
/// All three text fields are needed, not one. Jellyfin reports Atmos in the profile or in the
/// track title and never in the codec, which stays <c>eac3</c> or <c>truehd</c> - searching the
/// codec alone finds no Atmos at all on the reference library.
/// </para>
/// </remarks>
/// <param name="Codec">The codec, for example <c>dts</c>.</param>
/// <param name="Profile">The profile, for example <c>DTS-HD MA</c>.</param>
/// <param name="Title">The track title, which is where "Atmos" often hides.</param>
/// <param name="Channels">The channel count, or null when the stream does not say.</param>
/// <param name="Language">
/// The ISO code the stream carries, for example <c>deu</c>. Needed because the best track and the
/// interesting one are frequently not the same: measured on the reference library, 437 films carry
/// several languages in different formats, typically a German AC3 beside an English DTS. Reporting
/// the DTS there describes a track the owner of the library does not listen to.
/// </param>
internal readonly record struct AudioTrack(string? Codec, string? Profile, string? Title, int? Channels, string? Language = null);
