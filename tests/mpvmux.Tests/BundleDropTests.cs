using mpvmux.Services;
using Xunit;

namespace mpvmux.Tests;

public sealed class BundleDropTests
{
    [Fact]
    public void FindBundles_ReturnsTheOnlyBundle()
    {
        var result = BundleDropClassifier.FindBundles(["season-01.mpvm"]);

        Assert.Equal(["season-01.mpvm"], result);
    }

    [Fact]
    public void FindBundles_MatchesTheExtensionCaseInsensitively()
    {
        var result = BundleDropClassifier.FindBundles(["Season-01.MPVM"]);

        Assert.Equal(["Season-01.MPVM"], result);
    }

    [Fact]
    public void FindBundles_IgnoresMediaFilesInTheDrop()
    {
        var result = BundleDropClassifier.FindBundles(["episode-01.mkv", "season-01.mpvm", "episode-02.mkv"]);

        Assert.Equal(["season-01.mpvm"], result);
    }

    [Fact]
    public void FindBundles_KeepsTheDropOrder()
    {
        var result = BundleDropClassifier.FindBundles(["first.mpvm", "second.mpvm", "third.mpvm"]);

        Assert.Equal(["first.mpvm", "second.mpvm", "third.mpvm"], result);
    }

    [Fact]
    public void FindBundles_ReturnsEmptyWhenTheDropHasNoBundle()
    {
        Assert.Empty(BundleDropClassifier.FindBundles(["episode-01.mkv", "episode-02.srt"]));
    }

    [Fact]
    public void FindBundles_ReturnsEmptyForAFolderOrEmptyDrop()
    {
        Assert.Empty(BundleDropClassifier.FindBundles(["/media/episodes"]));
        Assert.Empty(BundleDropClassifier.FindBundles([]));
    }
}
