using Xunit;

namespace Jellyfin.Plugin.PosterOverlays.Tests;

/// <summary>
/// Groups every test class that assigns <c>BaseItem.MediaSourceManager</c>, so they run one after
/// another instead of side by side.
/// </summary>
/// <remarks>
/// That static is global to the process, and xUnit runs test classes in parallel by default. Two
/// classes setting it to different fakes therefore raced: each passed on its own and one failed in
/// the full suite, which is the worst shape a test failure can take - it looks like the code under
/// test and moves when you look at it.
/// <para>
/// A shared collection is the smallest fix that keeps the fakes meaningful. Handing every test the
/// same fake would work too, but then a test could no longer say what its item's streams are, and
/// that is the thing being tested.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BaseItemStaticsCollection
{
    public const string Name = "BaseItem statics";
}
