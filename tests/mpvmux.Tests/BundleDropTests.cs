using mpvmux.Services;
using Xunit;

namespace mpvmux.Tests;

public sealed class BundleDropTests
{
    [Fact]
    public void FindBundle_ReturnsTheOnlyBundle()
    {
        var result = BundleDropClassifier.FindBundle(["season-01.mpvm"]);

        Assert.Equal("season-01.mpvm", result);
    }

    [Fact]
    public void FindBundle_MatchesTheExtensionCaseInsensitively()
    {
        var result = BundleDropClassifier.FindBundle(["Season-01.MPVM"]);

        Assert.Equal("Season-01.MPVM", result);
    }

    [Fact]
    public void FindBundle_PrefersTheBundleOverOtherDroppedFiles()
    {
        var result = BundleDropClassifier.FindBundle(["episode-01.mkv", "season-01.mpvm", "episode-02.mkv"]);

        Assert.Equal("season-01.mpvm", result);
    }

    [Fact]
    public void FindBundle_ReturnsTheFirstBundleOfSeveral()
    {
        var result = BundleDropClassifier.FindBundle(["first.mpvm", "second.mpvm"]);

        Assert.Equal("first.mpvm", result);
    }

    [Fact]
    public void FindBundle_ReturnsNullWhenTheDropHasNoBundle()
    {
        var result = BundleDropClassifier.FindBundle(["episode-01.mkv", "episode-02.srt"]);

        Assert.Null(result);
    }

    [Fact]
    public void FindBundle_ReturnsNullForAFolderOrEmptyDrop()
    {
        Assert.Null(BundleDropClassifier.FindBundle(["/media/episodes"]));
        Assert.Null(BundleDropClassifier.FindBundle([]));
    }
}
