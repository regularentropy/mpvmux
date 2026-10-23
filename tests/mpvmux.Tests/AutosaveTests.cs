using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace mpvmux.Tests;

public sealed class AutosaveTests : IDisposable
{
    private readonly string _rootPath;
    private readonly BundleBackupStore _store;
    private readonly BundleRepository _repository;
    private readonly CountingRepository _countingRepository;
    private readonly BundleSession _session = new();
    private readonly BundleEditTracker _tracker;
    private readonly ConfigModel _config;
    private readonly BundleFileService _fileService;
    private readonly BundleAutoSaveService _autoSave;
    private readonly string _bundlePath;

    public AutosaveTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "mpvmux-autosave-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);

        _store = new BundleBackupStore(Path.Combine(_rootPath, "backups"), Path.Combine(_rootPath, "corrupt"));
        _tracker = new BundleEditTracker(_session);
        _repository = new BundleRepository(_store, _tracker);
        _countingRepository = new CountingRepository(_repository);

        _config = new ConfigModel { AutoSaveEnabled = true };

        _fileService = new BundleFileService(
            _countingRepository,
            _session,
            new HistoryService(Path.Combine(_rootPath, "history.json")),
            _tracker);

        _autoSave = new BundleAutoSaveService(
            _fileService,
            _countingRepository,
            _session,
            _tracker,
            new FakeConfigService(_config),
            TimeSpan.FromMilliseconds(40));

        _bundlePath = Path.Combine(_rootPath, "bundle.mpvm");
    }

    // ---- writing --------------------------------------------------------------

    [Fact]
    public async Task WritesTheBundleAfterTheDebounce()
    {
        await StartWithAVideoAsync("episode-01.mkv");

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        Assert.True(await WaitUntilAsync(() => !_tracker.IsDirty));
        Assert.Contains("episode-02", await File.ReadAllTextAsync(_bundlePath));
    }

    [Fact]
    public async Task ABurstOfEditsIsWrittenOnce()
    {
        await StartWithAVideoAsync("episode-01.mkv");
        _countingRepository.Saves = 0;

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));
        _session.Bundle.Volume = 60;
        _session.Bundle.SelectedVideoIndex = 0;
        _session.Bundle.VideoList[0] = new MediaRecord("episode-01", "/media/episode-01.renamed.mkv");

        Assert.True(await WaitUntilAsync(() => !_tracker.IsDirty));
        Assert.Equal(1, _countingRepository.Saves);
    }

    [Fact]
    public async Task DoesNothingWhenAutoSaveIsOff()
    {
        await StartWithAVideoAsync("episode-01.mkv");
        _config.AutoSaveEnabled = false;

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        await Task.Delay(200);

        Assert.True(_tracker.IsDirty);
        Assert.DoesNotContain("episode-02", await File.ReadAllTextAsync(_bundlePath));
    }

    [Fact]
    public async Task DoesNotWriteABundleWithoutADestination()
    {
        // a new bundle has no path until Save as, so there is nothing to write to
        _session.Bundle = new MediaBundleModel();
        _session.Bundle.VideoList.Add(new MediaRecord("episode-01", "/media/episode-01.mkv"));

        await Task.Delay(200);

        Assert.True(_tracker.IsDirty);
        Assert.Empty(_repository.SelectedFilePath);
        Assert.Equal(0, _countingRepository.Saves);
    }

    [Fact]
    public async Task DropsAPendingWriteWhenAnotherBundleOpens()
    {
        await StartWithAVideoAsync("episode-01.mkv");

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));
        _session.Bundle = new MediaBundleModel();

        await Task.Delay(200);

        Assert.DoesNotContain("episode-02", await File.ReadAllTextAsync(_bundlePath));
        Assert.False(_tracker.IsDirty);
    }

    // ---- backups --------------------------------------------------------------

    [Fact]
    public async Task AutoSaveKeepsTheBackupAtTheLastExplicitSave()
    {
        await StartWithAVideoAsync("episode-01.mkv");

        // an explicit save promotes the current content into the recovery slot
        await _fileService.SaveAsync();
        var backupAfterExplicitSave = await _store.ReadTextAsync(_bundlePath);
        Assert.NotNull(backupAfterExplicitSave);

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));
        Assert.True(await WaitUntilAsync(() => !_tracker.IsDirty));

        Assert.Contains("episode-02", await File.ReadAllTextAsync(_bundlePath));
        Assert.DoesNotContain("episode-02", await _store.ReadTextAsync(_bundlePath));

        // the next deliberate save is what advances the slot
        await _fileService.SaveAsync();
        Assert.Contains("episode-02", await _store.ReadTextAsync(_bundlePath));
    }

    // ---- failures and flushing -------------------------------------------------

    [Fact]
    public async Task ReportsAWriteFailureWithoutThrowing()
    {
        await StartWithAVideoAsync("episode-01.mkv");

        var failure = new TaskCompletionSource<string>();
        _autoSave.Failed += (_, message) => failure.TrySetResult(message);

        _countingRepository.FailWrites = true;
        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        var message = await failure.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.StartsWith("Could not save automatically:", message);
        Assert.True(_tracker.IsDirty);
    }

    [Fact]
    public async Task FlushWritesPendingEditsImmediately()
    {
        await StartWithAVideoAsync("episode-01.mkv");
        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        Assert.True(await _autoSave.FlushAsync());

        Assert.Contains("episode-02", await File.ReadAllTextAsync(_bundlePath));
        Assert.False(_tracker.IsDirty);
    }

    [Fact]
    public async Task FlushReportsthatTheBundleStillNeedsADestination()
    {
        _session.Bundle = new MediaBundleModel();
        _session.Bundle.VideoList.Add(new MediaRecord("episode-01", "/media/episode-01.mkv"));

        Assert.False(await _autoSave.FlushAsync());
        Assert.True(_tracker.IsDirty);
    }

    [Fact]
    public async Task FlushWithAutoSaveOffKeepsTheBundleDirty()
    {
        await StartWithAVideoAsync("episode-01.mkv");
        _config.AutoSaveEnabled = false;
        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        Assert.False(await _autoSave.FlushAsync());
        Assert.True(_tracker.IsDirty);
    }

    [Fact]
    public void IsEnabledMirrorsTheSetting()
    {
        Assert.True(_autoSave.IsEnabled);

        _config.AutoSaveEnabled = false;

        Assert.False(_autoSave.IsEnabled);
    }

    // ---- helpers ---------------------------------------------------------------

    private async Task StartWithAVideoAsync(string fileName)
    {
        _session.Bundle.VideoList.Add(new MediaRecord(Path.GetFileNameWithoutExtension(fileName), "/media/" + fileName));
        await _fileService.SaveAsAsync(_bundlePath);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(10);
        }

        return condition();
    }

    public void Dispose()
    {
        _autoSave.Cancel();

        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp directory must not fail the test run
        }
    }

    private sealed class FakeConfigService(ConfigModel config) : IConfigService
    {
        public ConfigModel Config { get; } = config;
        public bool IsDirty => false;
        public bool Save() => true;
        public SettingsRecoveryReport? TakeRecoveryReport() => null;
    }

    /// <summary>Counts writes and can be made to fail them, so the tests can observe both.</summary>
    private sealed class CountingRepository(IBundleRepository inner) : IBundleRepository
    {
        public int Saves { get; set; }

        public bool FailWrites { get; set; }

        public string SelectedFilePath => inner.SelectedFilePath;

        public event EventHandler? SelectedFilePathChanged
        {
            add => inner.SelectedFilePathChanged += value;
            remove => inner.SelectedFilePathChanged -= value;
        }

        public Task<BundleSaveResult> SaveAsync(
            MediaBundleModel bundle,
            BundleBackupMode backupMode = BundleBackupMode.Promote)
        {
            Saves++;

            return FailWrites
                ? throw new BundleSaveException("Could not write the bundle.")
                : inner.SaveAsync(bundle, backupMode);
        }

        public Task<BundleSaveResult> SaveAsAsync(MediaBundleModel bundle, string path)
        {
            Saves++;
            return inner.SaveAsAsync(bundle, path);
        }

        public Task<BundleLoadResult> LoadFromFileAsync(string filePath) => inner.LoadFromFileAsync(filePath);

        public Task<BundleLoadResult> RestoreBackupAsync(string filePath) => inner.RestoreBackupAsync(filePath);

        public void ClearSelectedFile() => inner.ClearSelectedFile();
    }
}
