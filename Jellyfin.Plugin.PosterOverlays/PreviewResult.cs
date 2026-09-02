namespace Jellyfin.Plugin.PosterOverlays;

/// <summary>
/// A rendered preview of one item, produced without changing anything.
/// </summary>
/// <remarks>
/// <see cref="Note"/> is the part that matters when <see cref="BadgeCount"/> is zero. A preview
/// that simply shows an unbadged poster is indistinguishable from one that failed, and the reason
/// is nearly always a deliberate rule - the category is off, the item is excepted, the episode has
/// no twin. Saying which one turns a confusing picture into an answer.
/// </remarks>
/// <para>
/// Internal on purpose. It carries the encoded image as a <c>byte[]</c>, which CA1819 rightly
/// objects to on a public surface - the array is handed straight to <c>File(...)</c> and copying
/// it per preview would be waste for no gain. Keeping the type inside the assembly answers the
/// rule where it applies instead of suppressing it where it does not.
/// </para>
/// <param name="Bytes">The image to show.</param>
/// <param name="MimeType">Its content type.</param>
/// <param name="BadgeCount">How many badges were drawn. Zero means the image is untouched.</param>
/// <param name="Note">Why nothing was drawn, or empty when something was.</param>
internal sealed record PreviewResult(byte[] Bytes, string MimeType, int BadgeCount, string Note);
