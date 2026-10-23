using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace mpvmux.Tests;

/// <summary>
/// Rename detection and the confirmed-apply path. Every test works on a GUID temp directory and
/// real files, because the heuristics are defined in terms of the filesystem.
/// </summary>
public sealed class MediaRelinkTests : IDisposable
{
    private readonly string _directory;
    private readonly MediaRelinkService _service = new();

    public MediaRelinkTests()
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

    // ---- helpers -------------------------------------------------------------------

    private string PathFor(params string[] parts) => Path.Combine([_directory, .. parts]);

    private string Create(string relativePath, string content = "media")
    {
        var path = PathFor(relativePath.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static MediaBundleModel BundleWithVideos(params MediaRecord[] videos)
    {
        var bundle = new MediaBundleModel();
        bundle.SetVideoList(videos.ToList());
        return bundle;
    }

    private static MediaRelinkProposal? ProposalFor(
        IReadOnlyList<MediaRelinkProposal> proposals,
        MediaSlot slot,
        int index) =>
        proposals.FirstOrDefault(proposal => proposal.Slot == slot && proposal.Index == index);

    private static MediaRecord Record(string path) => new(Path.GetFileNameWithoutExtension(path), path);

    // ---- matching ------------------------------------------------------------------

    [Fact]
    public async Task Detect_OffersAnExactNameMovedIntoASubfolder()
    {
        var moved = Create("season/Show.S01E01.mkv");
        var bundle = BundleWithVideos(Record(PathFor("Show.S01E01.mkv")));

        var proposals = await _service.DetectAsync(bundle);

        var candidate = Assert.Single(Assert.Single(proposals).Candidates);
        Assert.Equal(moved, candidate.Path);
        Assert.Equal(MediaRelinkReason.ExactName, candidate.Reason);
    }

    [Fact]
    public async Task Detect_RecognisesACaseOnlyRename()
    {
        // on Windows the original spelling resolves to the same file, so nothing is missing
        if (OperatingSystem.IsWindows())
            return;

        var actual = Create("Show.S01E01.mkv");
        var bundle = BundleWithVideos(Record(PathFor("show.s01e01.mkv")));

        var proposals = await _service.DetectAsync(bundle);

        var candidate = Assert.Single(Assert.Single(proposals).Candidates);
        Assert.Equal(actual, candidate.Path);
        Assert.Equal(MediaRelinkReason.CaseOnly, candidate.Reason);
    }

    [Fact]
    public async Task Detect_MatchesARenamedFileByContentHash()
    {
        var target = Create("renamed.mkv", "identical bytes");
        var hash = MediaFileFingerprint.ComputeContentHash(target);
        Assert.NotNull(hash);

        var missing = Record(PathFor("original.mkv")) with
        {
            ContentHash = hash,
            SizeBytes = new FileInfo(target).Length,
            // the timestamps deliberately differ, so only the content can identify the file
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };

        var proposals = await _service.DetectAsync(BundleWithVideos(missing));

        var candidate = Assert.Single(Assert.Single(proposals).Candidates);
        Assert.Equal(target, candidate.Path);
        Assert.Equal(MediaRelinkReason.ContentHash, candidate.Reason);
    }

    [Fact]
    public async Task Detect_ContentHashSurvivesAnExtensionChange()
    {
        var target = Create("Show.S01E01.mp4", "same stream bytes");
        var missing = Record(PathFor("Show.S01E01.mkv")) with
        {
            ContentHash = MediaFileFingerprint.ComputeContentHash(target),
            SizeBytes = new FileInfo(target).Length,
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };

        var proposals = await _service.DetectAsync(BundleWithVideos(missing));

        var candidate = Assert.Single(Assert.Single(proposals).Candidates);
        Assert.Equal(target, candidate.Path);
        Assert.Equal(MediaRelinkReason.ContentHash, candidate.Reason);
    }

    [Fact]
    public async Task Detect_MatchesAReEncodedFileByItsUnchangedName()
    {
        // the content changed, so no hash can match; the name is the remaining signal
        var reEncoded = Create("season/Show.S01E01.mkv", "a different encode");
        var missing = Record(PathFor("Show.S01E01.mkv")) with
        {
            ContentHash = "0000000000000000000000000000000000000000000000000000000000000000",
            SizeBytes = -1,
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };

        var proposals = await _service.DetectAsync(BundleWithVideos(missing));

        var candidate = Assert.Single(Assert.Single(proposals).Candidates);
        Assert.Equal(reEncoded, candidate.Path);
        Assert.Equal(MediaRelinkReason.ExactName, candidate.Reason);
    }

    [Fact]
    public async Task Detect_NeverProposesAFileAnotherCellStillUses()
    {
        var shared = Create("shared.mkv", "shared bytes");
        var hash = MediaFileFingerprint.ComputeContentHash(shared);

        var missing = Record(PathFor("missing.mkv")) with
        {
            ContentHash = hash,
            SizeBytes = new FileInfo(shared).Length,
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };

        var bundle = BundleWithVideos(missing);
        bundle.SetAudioList([Record(shared)]);

        var proposals = await _service.DetectAsync(bundle);

        Assert.Empty(Assert.Single(proposals).Candidates);
    }

    [Fact]
    public async Task Detect_AssignsOneFileToOneCellOnly()
    {
        var target = Create("target.mkv", "unique bytes");
        var hash = MediaFileFingerprint.ComputeContentHash(target);
        var length = new FileInfo(target).Length;

        var first = Record(PathFor("first.mkv")) with
        {
            ContentHash = hash,
            SizeBytes = length,
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };
        var second = Record(PathFor("second.mkv")) with
        {
            ContentHash = hash,
            SizeBytes = length,
            ModifiedUtc = DateTime.UtcNow.AddDays(-3)
        };

        var proposals = await _service.DetectAsync(BundleWithVideos(first, second));

        Assert.Equal(2, proposals.Count);
        Assert.Single(proposals, proposal => proposal.Candidates.Count == 1);
        Assert.Single(proposals, proposal => proposal.Candidates.Count == 0);
    }

    [Fact]
    public async Task Detect_ReportsMissingCellsWithoutCandidatesToo()
    {
        var found = Create("found.mkv", "unique bytes");
        var hash = MediaFileFingerprint.ComputeContentHash(found);

        var bundle = BundleWithVideos(
            Record(PathFor("missing.mkv")) with
            {
                ContentHash = hash,
                SizeBytes = new FileInfo(found).Length,
                ModifiedUtc = DateTime.UtcNow.AddDays(-3)
            },
            Record(PathFor("Somewhere.Else.S02E07.mkv")));

        var proposals = await _service.DetectAsync(bundle);

        Assert.Equal(2, proposals.Count);
        Assert.Single(proposals, proposal => proposal.Candidates.Count == 1);
    }

    [Fact]
    public async Task Detect_ReturnsNothingForAnUnreadableOrMissingFolder()
    {
        var bundle = BundleWithVideos(Record(PathFor("gone", "Show.S01E01.mkv")));

        var proposals = await _service.DetectAsync(bundle);

        Assert.Empty(Assert.Single(proposals).Candidates);
    }

    [Fact]
    public async Task Detect_ReturnsNoProposalsWhenEveryFileExists()
    {
        var video = Create("Show.S01E01.mkv");
        var bundle = BundleWithVideos(Record(video));

        var proposals = await _service.DetectAsync(bundle);

        Assert.Empty(proposals);
    }

    // ---- applying ------------------------------------------------------------------

    [Fact]
    public void Apply_RewritesTheCellAndRefreshesNameAndFingerprint()
    {
        var target = Create("Show.S01E01.2160p.mkv", "replacement bytes");
        var bundle = BundleWithVideos(Record(PathFor("Show.S01E01.1080p.mkv")));

        var applied = _service.Apply(bundle, [
            new MediaRelinkSelection(
                MediaSlot.Video, 0, PathFor("Show.S01E01.1080p.mkv"), target)
        ]);

        Assert.Equal(1, applied);
        var record = bundle.VideoList[0];
        Assert.Equal(target, record.Path);
        Assert.Equal("Show.S01E01.2160p", record.Name);
        Assert.Equal(new FileInfo(target).Length, record.SizeBytes);
        Assert.NotNull(record.ModifiedUtc);
    }

    [Fact]
    public void Apply_SkipsACellThatChangedWhileTheDialogWasOpen()
    {
        var target = Create("new.mkv");
        var bundle = BundleWithVideos(Record(PathFor("other.mkv")));

        var applied = _service.Apply(bundle, [
            new MediaRelinkSelection(MediaSlot.Video, 0, PathFor("old.mkv"), target)
        ]);

        Assert.Equal(0, applied);
        Assert.Equal(PathFor("other.mkv"), bundle.VideoList[0].Path);
    }

    [Fact]
    public void Apply_SkipsTargetsThatNoLongerExist()
    {
        var bundle = BundleWithVideos(Record(PathFor("old.mkv")));

        var applied = _service.Apply(bundle, [
            new MediaRelinkSelection(
                MediaSlot.Video, 0, PathFor("old.mkv"), PathFor("never-existed.mkv"))
        ]);

        Assert.Equal(0, applied);
        Assert.Equal(PathFor("old.mkv"), bundle.VideoList[0].Path);
    }

    [Fact]
    public void Apply_UsesOneFileForOneCellOnly()
    {
        var target = Create("target.mkv");
        var bundle = BundleWithVideos(
            Record(PathFor("first.mkv")),
            Record(PathFor("second.mkv")));

        var applied = _service.Apply(bundle, [
            new MediaRelinkSelection(MediaSlot.Video, 0, PathFor("first.mkv"), target),
            new MediaRelinkSelection(MediaSlot.Video, 1, PathFor("second.mkv"), target)
        ]);

        Assert.Equal(1, applied);
        Assert.Equal(target, bundle.VideoList[0].Path);
        Assert.Equal(PathFor("second.mkv"), bundle.VideoList[1].Path);
    }

    [Fact]
    public void Apply_IgnoresSelectionsOutsideTheList()
    {
        var target = Create("target.mkv");
        var bundle = BundleWithVideos(Record(PathFor("old.mkv")));

        var applied = _service.Apply(bundle, [
            new MediaRelinkSelection(MediaSlot.Audio, 5, PathFor("old.flac"), target)
        ]);

        Assert.Equal(0, applied);
    }
}
