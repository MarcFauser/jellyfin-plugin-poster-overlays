using System;
using System.Collections.Generic;
using System.Reflection;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// The one piece of Jellyfin a <see cref="MediaBrowser.Controller.Entities.BaseItem"/> reaches for
/// on its own: <c>GetMediaStreams()</c> goes through the static
/// <c>BaseItem.MediaSourceManager</c>, so an item built in a test throws a NullReferenceException
/// from inside the framework before any plugin code is reached.
/// </summary>
/// <remarks>
/// A <see cref="DispatchProxy"/> rather than twenty-one hand-written stubs: the interface has 21
/// members, exactly one of which is used here, and a stub file would have to be revisited every
/// time Jellyfin adds a method to an interface this project does not otherwise care about.
/// <para>
/// Everything except the stream lookup throws. That is deliberate - a fake that quietly returns
/// null for calls nobody planned for turns a wrong test into a passing one, which is the failure
/// mode this whole test class exists to catch.
/// </para>
/// </remarks>
// Not sealed on purpose: DispatchProxy.Create generates a subclass at run time and rejects a
// sealed base with "The base type ... cannot be sealed".
internal class FakeMediaSourceManager : DispatchProxy
{
    private IReadOnlyList<MediaStream> _streams = [];

    public static IMediaSourceManager WithStreams(params MediaStream[] streams)
    {
        object proxy = Create<IMediaSourceManager, FakeMediaSourceManager>()!;
        ((FakeMediaSourceManager)proxy)._streams = streams;
        return (IMediaSourceManager)proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IMediaSourceManager.GetMediaStreams))
        {
            return new List<MediaStream>(_streams);
        }

        throw new NotSupportedException(
            $"The test fake was asked for {targetMethod?.Name}, which no test has set up.");
    }
}
