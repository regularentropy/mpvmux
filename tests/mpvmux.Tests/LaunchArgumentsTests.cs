using mpvmux.Services;
using System;
using System.IO;
using Xunit;

namespace mpvmux.Tests;

public sealed class LaunchArgumentsTests : IDisposable
{
    private readonly string _rootPath;

    public LaunchArgumentsTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "mpvmux-args-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(_rootPath, name);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void ExistingBundlePathsAreReturnedInArgumentOrder()
    {
        var first = NewFile("first.mpvm");
        var second = NewFile("second.mpvm");

        var result = LaunchArguments.BundlePaths([first, second]);

        Assert.Equal([first, second], result);
    }

    [Fact]
    public void MissingFilesAreIgnored()
    {
        var existing = NewFile("existing.mpvm");

        var result = LaunchArguments.BundlePaths([Path.Combine(_rootPath, "gone.mpvm"), existing]);

        Assert.Equal([existing], result);
    }

    [Fact]
    public void ArgumentsThatAreNotBundlesAreIgnored()
    {
        var bundle = NewFile("episode.mpvm");
        var media = NewFile("episode.mkv");

        var result = LaunchArguments.BundlePaths(["--verbose", media, "", "   ", bundle]);

        Assert.Equal([bundle], result);
    }

    [Fact]
    public void RepeatedPathsAreReturnedOnce()
    {
        var bundle = NewFile("episode.mpvm");

        var result = LaunchArguments.BundlePaths([bundle, bundle]);

        Assert.Equal([bundle], result);
    }

    [Fact]
    public void NoArgumentsYieldNothing()
    {
        Assert.Empty(LaunchArguments.BundlePaths(null));
        Assert.Empty(LaunchArguments.BundlePaths([]));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp directory must not fail the test run
        }
    }
}
