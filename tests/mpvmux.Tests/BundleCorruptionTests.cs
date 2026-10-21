using mpvmux.Models;
using mpvmux.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace mpvmux.Tests;

public sealed class BundleCorruptionTests : IDisposable
{
    private readonly string _directory;
    private readonly string _backupDirectory;
    private readonly string _corruptDirectory;
    private readonly BundleBackupStore _store;

    public BundleCorruptionTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "mpvmux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        // never touch the real %APPDATA%\mpvmux from tests
        _backupDirectory = Path.Combine(_directory, "backups");
        _corruptDirectory = Path.Combine(_directory, "corrupt");
        _store = new BundleBackupStore(_backupDirectory, _corruptDirectory);
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

    private string PathFor(string name) => Path.Combine(_directory, name);

    private BundleRepository NewRepository() => new(_store);

    private sealed class FailingWriteStore(IBundleBackupStore inner) : IBundleBackupStore
    {
        public string KeyFor(string bundlePath) => inner.KeyFor(bundlePath);
        public bool Exists(string bundlePath) => inner.Exists(bundlePath);
        public Task<bool> WriteAsync(string bundlePath, string content) => Task.FromResult(false);
        public Task<BundleLoadResult> ReadAsync(string bundlePath) => inner.ReadAsync(bundlePath);
        public Task<string?> ReadTextAsync(string bundlePath) => inner.ReadTextAsync(bundlePath);
        public string Quarantine(string bundlePath) => inner.Quarantine(bundlePath);
    }

    private static MediaBundleModel BundleWithRow(string videoPath, int volume = 100)
    {
        var bundle = new MediaBundleModel { Volume = volume };
        bundle.SetVideoList([new MediaRecord("video", videoPath)]);
        return bundle;
    }

    // ---- parsing and normalization ------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("""{"VideoList":[}""")]
    [InlineData("not json at all")]
    public void Parse_RejectsMalformedJson(string text)
    {
        var result = BundleSerializer.Parse(text, "broken.mpvm");

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.InvalidJson, result.Error);
        Assert.Null(result.Bundle);
    }

    [Fact]
    public void Parse_RejectsNullRoot()
    {
        var result = BundleSerializer.Parse("null", "broken.mpvm");

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.InvalidShape, result.Error);
    }

    [Fact]
    public void Parse_RejectsArrayRoot()
    {
        var result = BundleSerializer.Parse("[]", "broken.mpvm");

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.InvalidJson, result.Error);
    }

    [Fact]
    public void Parse_ReplacesNullCollections()
    {
        var result = BundleSerializer.Parse(
            """{"VideoList":null,"AudioList":null,"SubtitleList":null}""",
            "bundle.mpvm");

        Assert.True(result.IsSuccess);
        var bundle = result.Bundle!;
        Assert.NotNull(bundle.VideoList);
        Assert.NotNull(bundle.AudioList);
        Assert.NotNull(bundle.SubtitleList);
        Assert.Equal(-1, bundle.SelectedVideoIndex);
        Assert.Equal(100, bundle.Volume);
    }

    [Fact]
    public void Parse_NormalizesNullEntriesTrimsTextAndDerivesNames()
    {
        var result = BundleSerializer.Parse(
            """
            {
              "VideoList": [
                { "Name": "  ", "Path": "  /media/one.mkv  " },
                { "Name": "Second", "Path": "/media/two.mkv" }
              ],
              "AudioList": [ null, { "Name": "Track", "Path": " /media/a.flac " } ],
              "SubtitleList": null,
              "SelectedVideoIndex": 99,
              "Volume": 9000
            }
            """,
            "bundle.mpvm");

        Assert.True(result.IsSuccess);
        var bundle = result.Bundle!;

        Assert.Equal(2, bundle.VideoList.Count);
        Assert.Equal("one", bundle.VideoList[0].Name);
        Assert.Equal("/media/one.mkv", bundle.VideoList[0].Path);

        // the null entry becomes an aligned placeholder instead of crashing the UI
        Assert.Equal(2, bundle.AudioList.Count);
        Assert.Equal("", bundle.AudioList[0].Path);
        Assert.Equal("/media/a.flac", bundle.AudioList[1].Path);

        Assert.Equal(1, bundle.SelectedVideoIndex);
        Assert.Equal(100, bundle.Volume);
    }

    [Fact]
    public void Parse_ClampsVolumeAndSelectionToTheirRanges()
    {
        var result = BundleSerializer.Parse(
            """
            {
              "VideoList": [ { "Name": "A", "Path": "/a" } ],
              "SelectedVideoIndex": -99,
              "Volume": -42
            }
            """,
            "bundle.mpvm");

        Assert.True(result.IsSuccess);
        Assert.Equal(-1, result.Bundle!.SelectedVideoIndex);
        Assert.Equal(0, result.Bundle.Volume);
    }

    [Fact]
    public void Parse_RemovesDuplicateAndEmptyRowsWithoutBreakingAlignment()
    {
        var result = BundleSerializer.Parse(
            """
            {
              "VideoList": [
                { "Name": "A", "Path": "/a" },
                { "Name": "A", "Path": "/a" },
                { "Name": "B", "Path": "/b" }
              ],
              "AudioList": [
                { "Name": "one", "Path": "/a.flac" },
                { "Name": "one", "Path": "/a.flac" },
                { "Name": "two", "Path": "/b.flac" }
              ],
              "SubtitleList": [],
              "SelectedVideoIndex": 0
            }
            """,
            "bundle.mpvm");

        Assert.True(result.IsSuccess);
        var bundle = result.Bundle!;

        Assert.Equal(2, bundle.VideoList.Count);
        Assert.Equal(2, bundle.AudioList.Count);
        Assert.Equal("B", bundle.VideoList[1].Name);
        Assert.Equal("/b.flac", bundle.AudioList[1].Path);
    }

    [Fact]
    public void Parse_TreatsMissingSchemaVersionAsVersionOne()
    {
        var result = BundleSerializer.Parse("""{"VideoList":[]}""", "bundle.mpvm");

        Assert.True(result.IsSuccess);
        Assert.Equal(BundleSerializer.CurrentSchemaVersion, result.Bundle!.SchemaVersion);
    }

    [Fact]
    public void Parse_RejectsNewerSchemaVersion()
    {
        var result = BundleSerializer.Parse(
            """{"SchemaVersion":99,"VideoList":[]}""",
            "bundle.mpvm");

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.NewerSchema, result.Error);
        Assert.Null(result.Bundle);
    }

    [Fact]
    public void Serialize_StampsCurrentSchemaVersion()
    {
        var bundle = new MediaBundleModel { SchemaVersion = 99 };

        var json = BundleSerializer.Serialize(bundle);

        Assert.Equal(BundleSerializer.CurrentSchemaVersion, bundle.SchemaVersion);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            BundleSerializer.CurrentSchemaVersion,
            document.RootElement.GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public async Task Load_RejectsOversizedFile()
    {
        var path = PathFor("huge.mpvm");
        await using (var stream = File.Create(path))
            stream.SetLength(BundleSerializer.MaxBundleBytes + 1);

        var result = await BundleSerializer.LoadAsync(path);

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.TooLarge, result.Error);
    }

    [Fact]
    public async Task Load_ReportsMissingFile()
    {
        var result = await BundleSerializer.LoadAsync(PathFor("gone.mpvm"));

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.Missing, result.Error);
    }

    // ---- atomic writes and backups -------------------------------------------------

    [Fact]
    public async Task AtomicWrite_ReplacesContentAndLeavesNoTempFile()
    {
        var path = PathFor("atomic.mpvm");
        await File.WriteAllTextAsync(AtomicFile.TempPathFor(path), "stale");

        await AtomicFile.WriteAllTextAsync(path, "content");

        Assert.Equal("content", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(AtomicFile.TempPathFor(path)));
    }

    [Fact]
    public async Task Save_PromotesThePreviousValidFileToTheStore()
    {
        var path = PathFor("show.mpvm");
        var repository = NewRepository();
        var bundle = BundleWithRow("/media/video.mkv", volume: 100);

        await repository.SaveAsAsync(bundle, path);
        Assert.True(File.Exists(path));
        Assert.False(_store.Exists(path));

        bundle.Volume = 50;
        await repository.SaveAsync(bundle);

        var backup = await _store.ReadAsync(path);
        Assert.True(backup.IsSuccess);
        Assert.Equal(100, backup.Bundle!.Volume);

        var current = await BundleSerializer.LoadAsync(path);
        Assert.True(current.IsSuccess);
        Assert.Equal(50, current.Bundle!.Volume);

        Assert.Equal(path, repository.SelectedFilePath);
        Assert.False(File.Exists(AtomicFile.TempPathFor(path)));
    }

    [Fact]
    public async Task Save_CreatesNoSidecarBesideTheBundle()
    {
        var path = PathFor("show.mpvm");
        var repository = NewRepository();
        var bundle = BundleWithRow("/media/video.mkv", volume: 100);

        await repository.SaveAsAsync(bundle, path);
        bundle.Volume = 50;
        await repository.SaveAsync(bundle);

        Assert.Single(Directory.GetFiles(_directory, "show.mpvm*"));
        Assert.False(File.Exists(path + ".bak"));
        Assert.True(File.Exists(_store.BackupPathFor(path)));
        Assert.StartsWith(_backupDirectory, _store.BackupPathFor(path));
    }

    [Fact]
    public async Task Save_DoesNotReplaceAnOlderGoodBackupWithACorruptFile()
    {
        var path = PathFor("show.mpvm");
        var repository = NewRepository();
        var bundle = BundleWithRow("/media/video.mkv", volume: 100);

        await repository.SaveAsAsync(bundle, path);
        bundle.Volume = 50;
        await repository.SaveAsync(bundle); // store = volume 100

        await File.WriteAllTextAsync(path, "{ this is not a bundle");
        bundle.Volume = 25;
        await repository.SaveAsync(bundle);

        var backup = await _store.ReadAsync(path);
        Assert.True(backup.IsSuccess);
        Assert.Equal(100, backup.Bundle!.Volume);

        var current = await BundleSerializer.LoadAsync(path);
        Assert.True(current.IsSuccess);
        Assert.Equal(25, current.Bundle!.Volume);
    }

    [Fact]
    public async Task Save_RefusesToOverwriteANewerSchema()
    {
        var path = PathFor("future.mpvm");
        var repository = NewRepository();
        await repository.SaveAsAsync(BundleWithRow("/media/video.mkv"), path);

        var newer = """{"SchemaVersion":99,"VideoList":[]}""";
        await File.WriteAllTextAsync(path, newer);

        await Assert.ThrowsAsync<BundleSaveException>(
            () => repository.SaveAsync(BundleWithRow("/media/other.mkv")));

        Assert.Equal(newer, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Save_ReportsAWarningWhenTheBackupCannotBeWritten()
    {
        var path = PathFor("warning.mpvm");
        var repository = new BundleRepository(new FailingWriteStore(_store));

        await repository.SaveAsAsync(BundleWithRow("/media/video.mkv", volume: 100), path);
        var result = await repository.SaveAsync(BundleWithRow("/media/video.mkv", volume: 50));

        Assert.True(result.HasWarning);
        Assert.Contains("backup could not be updated", result.Warning);
        Assert.False(_store.Exists(path));

        // the primary save still wins
        var current = await BundleSerializer.LoadAsync(path);
        Assert.True(current.IsSuccess);
        Assert.Equal(50, current.Bundle!.Volume);
    }

    [Fact]
    public async Task BackupStore_KeepsWorkingWhenTheBundleDirectoryIsReadOnly()
    {
        // Windows read-only directories still accept writes, and root ignores the mode bits
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            return;

        var bundleDirectory = PathFor("media");
        Directory.CreateDirectory(bundleDirectory);
        var path = Path.Combine(bundleDirectory, "show.mpvm");
        var bundle = BundleWithRow("/media/video.mkv", volume: 100);
        await File.WriteAllTextAsync(path, BundleSerializer.Serialize(bundle));

        var mode = File.GetUnixFileMode(bundleDirectory);
        File.SetUnixFileMode(
            bundleDirectory,
            mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));

        try
        {
            Assert.True(await _store.WriteAsync(path, BundleSerializer.Serialize(bundle)));

            var backup = await _store.ReadAsync(path);
            Assert.True(backup.IsSuccess);
            Assert.False(_store.BackupPathFor(path).StartsWith(bundleDirectory));
        }
        finally
        {
            File.SetUnixFileMode(bundleDirectory, mode);
        }
    }

    // ---- failed loads and restore --------------------------------------------------

    [Fact]
    public async Task FailedLoad_LeavesTheSelectedFileAndSessionAlone()
    {
        var good = PathFor("good.mpvm");
        var bad = PathFor("bad.mpvm");
        var repository = NewRepository();

        await repository.SaveAsAsync(BundleWithRow("/media/good.mkv"), good);
        await File.WriteAllTextAsync(bad, "{ broken");

        var result = await repository.LoadFromFileAsync(bad);

        Assert.False(result.IsSuccess);
        Assert.Equal(BundleLoadError.InvalidJson, result.Error);
        Assert.Equal(good, repository.SelectedFilePath);
        Assert.False(result.CanRestoreBackup);
    }

    [Fact]
    public async Task Load_OffersTheStoredBackupAsARestoreCandidate()
    {
        var path = PathFor("load.mpvm");
        var repository = NewRepository();

        await repository.SaveAsAsync(BundleWithRow("/media/video.mkv"), path);
        await repository.SaveAsync(BundleWithRow("/media/video.mkv", volume: 50));

        await File.WriteAllTextAsync(path, "{ broken");
        var result = await repository.LoadFromFileAsync(path);

        Assert.False(result.IsSuccess);
        Assert.True(result.CanRestoreBackup);
    }

    [Fact]
    public async Task Restore_QuarantinesTheCorruptFileIntoTheStoreAndRestoresTheBackup()
    {
        var path = PathFor("restore.mpvm");
        var repository = NewRepository();
        var bundle = BundleWithRow("/media/video.mkv", volume: 100);

        await repository.SaveAsAsync(bundle, path);
        bundle.Volume = 60;
        await repository.SaveAsync(bundle); // store = volume 100

        await File.WriteAllTextAsync(path, "garbage");
        var failed = await repository.LoadFromFileAsync(path);
        Assert.True(failed.CanRestoreBackup);

        var restored = await repository.RestoreBackupAsync(path);

        Assert.True(restored.IsSuccess);
        Assert.Equal(100, restored.Bundle!.Volume);
        Assert.Single(Directory.GetFiles(_corruptDirectory, "*.mpvm.corrupt"));

        // no quarantine file beside the bundle, and the backup survives the restore
        Assert.Empty(Directory.GetFiles(_directory, "restore.mpvm.corrupt-*"));
        Assert.True(_store.Exists(path));

        var current = await BundleSerializer.LoadAsync(path);
        Assert.True(current.IsSuccess);
        Assert.Equal(100, current.Bundle!.Volume);
    }

    [Fact]
    public async Task Restore_LeavesTheOriginalAloneWhenTheBackupIsAlsoCorrupt()
    {
        var path = PathFor("restore.mpvm");
        var repository = NewRepository();

        await File.WriteAllTextAsync(path, "garbage");
        Directory.CreateDirectory(_backupDirectory);
        await File.WriteAllTextAsync(_store.BackupPathFor(path), "also garbage");

        var restored = await repository.RestoreBackupAsync(path);

        Assert.False(restored.IsSuccess);
        Assert.Equal("garbage", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_directory, "restore.mpvm.corrupt-*"));
        Assert.False(Directory.Exists(_corruptDirectory) && Directory.GetFiles(_corruptDirectory).Length > 0);
    }

    // ---- legacy sidecars -----------------------------------------------------------

    [Fact]
    public async Task Load_OffersAValidLegacySidecarWhenTheStoreIsEmpty()
    {
        var path = PathFor("legacy.mpvm");
        var repository = NewRepository();

        await File.WriteAllTextAsync(path, "{ broken");
        await File.WriteAllTextAsync(path + ".bak", BundleSerializer.Serialize(BundleWithRow("/media/legacy.mkv")));

        var result = await repository.LoadFromFileAsync(path);

        Assert.True(result.CanRestoreBackup);
    }

    [Fact]
    public async Task Save_ImportsALegacySidecarIntoTheStoreAndLeavesItInPlace()
    {
        var path = PathFor("legacy.mpvm");
        var repository = NewRepository();
        var legacyText = BundleSerializer.Serialize(BundleWithRow("/media/legacy.mkv", volume: 77));

        await repository.SaveAsAsync(BundleWithRow("/media/legacy.mkv", volume: 10), path);
        await File.WriteAllTextAsync(path + ".bak", legacyText);

        await repository.SaveAsync(BundleWithRow("/media/legacy.mkv", volume: 20));

        var backup = await _store.ReadAsync(path);
        Assert.True(backup.IsSuccess);
        Assert.Equal(77, backup.Bundle!.Volume);

        // legacy files are read-only inputs for this change
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public async Task Restore_ImportsALegacySidecarIntoTheStoreAndLeavesItInPlace()
    {
        var path = PathFor("legacy.mpvm");
        var repository = NewRepository();

        await File.WriteAllTextAsync(path, "garbage");
        await File.WriteAllTextAsync(
            path + ".bak",
            BundleSerializer.Serialize(BundleWithRow("/media/legacy.mkv", volume: 77)));

        var restored = await repository.RestoreBackupAsync(path);

        Assert.True(restored.IsSuccess);
        Assert.Equal(77, restored.Bundle!.Volume);
        Assert.True(_store.Exists(path));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Empty(Directory.GetFiles(_directory, "legacy.mpvm.corrupt-*"));
        Assert.Single(Directory.GetFiles(_corruptDirectory, "*.mpvm.corrupt"));
    }

    // ---- path keying ---------------------------------------------------------------

    [Fact]
    public void KeyFor_MapsDifferentSpellingsOfOnePathToOneSlot()
    {
        var path = PathFor("show.mpvm");
        var alternate = Path.Combine(_directory, ".", "sub", "..", "show.mpvm");

        Assert.Equal(_store.KeyFor(path), _store.KeyFor(alternate));
        Assert.Equal(_store.KeyFor(path), _store.KeyFor(Path.GetRelativePath(Environment.CurrentDirectory, path)));
    }

    [Fact]
    public void KeyFor_IgnoresCasingOnWindows()
    {
        var path = PathFor("Show.mpvm");
        var expected = Path.GetFullPath(path);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                _store.KeyFor(path),
                _store.KeyFor(expected.ToLowerInvariant()));
        }
        else
        {
            // case-sensitive file systems keep distinct slots for distinct names
            Assert.NotEqual(
                _store.KeyFor(path),
                _store.KeyFor(expected.ToLowerInvariant()));
        }
    }
}
