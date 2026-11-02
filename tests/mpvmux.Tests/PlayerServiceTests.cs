using mpvmux.Models;
using mpvmux.Services;
using System;
using System.IO;
using Xunit;

namespace mpvmux.Tests;

public sealed class PlayerServiceTests : IDisposable
{
    private readonly string _directory;

    public PlayerServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mpvmux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // path -> argv -> mpv: an option-shaped bundle path must not become an mpv option
    [Fact]
    public void BuildArgumentsRejectsOptionShapedVideoPath()
    {
        var bundle = Bundle("--log-file=" + Path.Combine(_directory, "victim.txt"));

        Assert.Throws<ArgumentException>(() => PlayerService.BuildArguments(bundle, fullscreen: false));
    }

    [Fact]
    public void BuildArgumentsRejectsOptionShapedAudioAndSubtitlePaths()
    {
        Assert.Throws<ArgumentException>(() => PlayerService.BuildArguments(
            Bundle(ExistingFile("clip.mp4"), audioPath: "--script=" + Path.Combine(_directory, "payload.lua")),
            fullscreen: false));

        Assert.Throws<ArgumentException>(() => PlayerService.BuildArguments(
            Bundle(ExistingFile("clip.mp4"), subtitlePath: "--input-ipc-server=" + Path.Combine(_directory, "s.sock")),
            fullscreen: false));
    }

    [Fact]
    public void BuildArgumentsRejectsMissingVideoFile()
    {
        var bundle = Bundle(Path.Combine(_directory, "gone.mp4"));

        Assert.Throws<ArgumentException>(() => PlayerService.BuildArguments(bundle, fullscreen: false));
    }

    // happy path: a real media row still produces the expected argv
    [Fact]
    public void BuildArgumentsPassesExistingVideoFile()
    {
        var video = ExistingFile("clip.mp4");
        var arguments = PlayerService.BuildArguments(Bundle(video), fullscreen: true);

        Assert.Equal(["--fullscreen", "--no-sub-auto", "--volume=100", video], arguments);
    }

    private string ExistingFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "media");
        return path;
    }

    private static MediaBundleModel Bundle(string videoPath, string? audioPath = null, string? subtitlePath = null)
    {
        var bundle = new MediaBundleModel { Volume = 100, SelectedVideoIndex = 0 };
        bundle.VideoList.Add(new MediaRecord("video", videoPath));

        if (audioPath is not null)
            bundle.AudioList.Add(new MediaRecord("audio", audioPath));
        if (subtitlePath is not null)
            bundle.SubtitleList.Add(new MediaRecord("subtitle", subtitlePath));

        return bundle;
    }
}
