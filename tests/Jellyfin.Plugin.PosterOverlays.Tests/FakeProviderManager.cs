using System;
using System.IO;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// A provider manager whose upload lands on the item and then dies - the server going down in the
/// instant after Jellyfin stored the image and before the plugin could write its record.
/// </summary>
/// <remarks>
/// Built the same way as <see cref="FakeMediaSourceManager"/>, and for the same reason: everything
/// but the one call under test throws, so a test cannot pass by reaching something nobody set up.
/// <para>
/// The image is written byte for byte to the path the item already points at, which is what
/// Jellyfin's <c>ImageSaver</c> does for an item whose image lives in its metadata folder.
/// </para>
/// </remarks>
// Not sealed: DispatchProxy.Create generates a subclass at run time.
internal class FakeProviderManager : DispatchProxy
{
    /// <summary>
    /// Gets how many uploads were attempted.
    /// </summary>
    public int Uploads { get; private set; }

    private bool Dies { get; set; }

    private bool Lands { get; set; }

    /// <summary>
    /// A provider manager whose upload lands and then throws.
    /// </summary>
    /// <param name="fake">The fake itself, to read <see cref="Uploads"/> from.</param>
    /// <returns>The proxy.</returns>
    public static IProviderManager DyingAfterTheUpload(out FakeProviderManager fake)
    {
        object proxy = Create<IProviderManager, FakeProviderManager>()!;
        fake = (FakeProviderManager)proxy;
        fake.Dies = true;
        fake.Lands = true;
        return (IProviderManager)proxy;
    }

    /// <summary>
    /// A provider manager that throws before anything reaches the item.
    /// </summary>
    /// <param name="fake">The fake itself, to read <see cref="Uploads"/> from.</param>
    /// <returns>The proxy.</returns>
    public static IProviderManager DyingBeforeTheUpload(out FakeProviderManager fake)
    {
        object proxy = Create<IProviderManager, FakeProviderManager>()!;
        fake = (FakeProviderManager)proxy;
        fake.Dies = true;
        return (IProviderManager)proxy;
    }

    /// <summary>
    /// A provider manager that must not be called at all.
    /// </summary>
    /// <param name="fake">The fake itself, to read <see cref="Uploads"/> from.</param>
    /// <returns>The proxy.</returns>
    public static IProviderManager NeverCalled(out FakeProviderManager fake)
    {
        object proxy = Create<IProviderManager, FakeProviderManager>()!;
        fake = (FakeProviderManager)proxy;
        return (IProviderManager)proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (Dies
            && targetMethod?.Name == nameof(IProviderManager.SaveImage)
            && args is { Length: 6 } && args[0] is BaseItem item && args[1] is Stream source)
        {
            Uploads++;
            if (!Lands)
            {
                throw new IOException("Simulated: the server died before the image reached the item.");
            }

            string path = item.GetImagePath(ImageType.Primary, 0)
                ?? throw new InvalidOperationException("The test item has no image path to write to.");
            using (var target = File.Create(path))
            {
                source.CopyTo(target);
            }

            throw new IOException("Simulated: the server died right after the image was stored.");
        }

        throw new NotSupportedException(
            $"The test fake was asked for {targetMethod?.Name}, which no test has set up.");
    }
}
