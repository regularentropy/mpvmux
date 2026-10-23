using mpvmux.Services;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace mpvmux.Tests;

/// <summary>
/// A multi-bundle drop opens the first bundle and registers the others in the recent list;
/// these tests exercise that registration against the real history service.
/// </summary>
public sealed class BundleDropHistoryTests : IDisposable
{
    private readonly string _rootPath;
    private readonly HistoryService _historyService;
    private readonly BundleFileService _fileService;

    public BundleDropHistoryTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "mpvmux-drop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);

        _historyService = new HistoryService(Path.Combine(_rootPath, "history.json"));
        var backupStore = new BundleBackupStore(
            Path.Combine(_rootPath, "backups"),
            Path.Combine(_rootPath, "corrupt"));
        _fileService = new BundleFileService(
            new BundleRepository(backupStore),
            new BundleSession(),
            _historyService);
    }

    [Fact]
    public void RememberBundles_RegistersTheDropOrder()
    {
        _fileService.RememberBundles(["first.mpvm", "second.mpvm", "third.mpvm"]);

        Assert.Equal(
            ["first.mpvm", "second.mpvm", "third.mpvm"],
            _historyService.HistoryModel.History.Select(entry => entry.Path));
    }

    [Fact]
    public void RememberBundles_KeepsExistingEntriesBehindTheDroppedGroup()
    {
        _fileService.RememberBundles(["old.mpvm"]);

        _fileService.RememberBundles(["first.mpvm", "second.mpvm"]);

        Assert.Equal(
            ["first.mpvm", "second.mpvm", "old.mpvm"],
            _historyService.HistoryModel.History.Select(entry => entry.Path));
    }

    [Fact]
    public void RememberBundles_IsIdempotentForTheSameDrop()
    {
        _fileService.RememberBundles(["first.mpvm", "second.mpvm"]);
        _fileService.RememberBundles(["first.mpvm", "second.mpvm"]);

        Assert.Equal(
            ["first.mpvm", "second.mpvm"],
            _historyService.HistoryModel.History.Select(entry => entry.Path));
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
