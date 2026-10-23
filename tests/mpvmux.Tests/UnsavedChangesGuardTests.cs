using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace mpvmux.Tests;

public sealed class UnsavedChangesGuardTests
{
    private readonly FakeEditTracker _tracker = new();
    private readonly FakeAutoSave _autoSave = new();
    private readonly FakeBundleFileService _fileService = new();
    private readonly FakeBundleRepository _repository = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeWindowService _windows = new();
    private readonly FakeDialogHelper _dialogHelper = new();

    private UnsavedChangesGuard CreateGuard() => new(
        _tracker,
        _autoSave,
        _fileService,
        _repository,
        _picker,
        _windows,
        _dialogHelper);

    // ---- already handled -------------------------------------------------------

    [Fact]
    public async Task ACleanBundleProceedsWithoutAsking()
    {
        _tracker.IsDirty = false;

        Assert.True(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Null(_windows.LastPrompt);
        Assert.Equal(0, _fileService.Saves);
        Assert.Equal(0, _autoSave.Flushes);
    }

    [Fact]
    public async Task AutosaveWritesThePendingEditsSoNobodyIsAsked()
    {
        _tracker.IsDirty = true;
        _autoSave.FlushResult = true;

        Assert.True(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal(1, _autoSave.Flushes);
        Assert.Null(_windows.LastPrompt);
        Assert.Equal(0, _fileService.Saves);
    }

    // ---- asking -----------------------------------------------------------------

    [Fact]
    public async Task CancellingKeepsTheBundleAndWritesNothing()
    {
        _tracker.IsDirty = true;
        _autoSave.FlushResult = false;
        _repository.SelectedFilePath = "/media/episode-01.mpvm";
        _windows.Choice = UnsavedChangesChoice.Cancel;

        Assert.False(await CreateGuard().ConfirmProceedAsync("opening another bundle"));
        Assert.Equal(0, _fileService.Saves);

        // the dialog names the bundle and the action
        Assert.Contains("episode-01", _windows.LastPrompt);
        Assert.Contains("opening another bundle", _windows.LastPrompt);
    }

    [Fact]
    public async Task DiscardingProceedsWithoutWriting()
    {
        _tracker.IsDirty = true;
        _windows.Choice = UnsavedChangesChoice.Discard;

        Assert.True(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal(0, _fileService.Saves);
    }

    [Fact]
    public async Task SavingWritesTheSelectedPath()
    {
        _tracker.IsDirty = true;
        _repository.SelectedFilePath = "/media/episode-01.mpvm";
        _windows.Choice = UnsavedChangesChoice.Save;

        Assert.True(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal(1, _fileService.Saves);
        Assert.Equal(0, _fileService.SaveAs);
    }

    [Fact]
    public async Task SavingABundleWithNoDestinationAsksForOne()
    {
        _tracker.IsDirty = true;
        _repository.SelectedFilePath = "";
        _windows.Choice = UnsavedChangesChoice.Save;
        _picker.Path = "/media/new-bundle.mpvm";

        Assert.True(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal(1, _fileService.SaveAs);
        Assert.Equal("/media/new-bundle.mpvm", _fileService.LastSaveAsPath);

        // a bundle without a destination cannot name itself
        Assert.Contains("this bundle", _windows.LastPrompt);
    }

    [Fact]
    public async Task CancellingTheDestinationPickerKeepsTheBundle()
    {
        _tracker.IsDirty = true;
        _repository.SelectedFilePath = "";
        _windows.Choice = UnsavedChangesChoice.Save;
        _picker.Path = null;

        Assert.False(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal(0, _fileService.SaveAs);
    }

    [Fact]
    public async Task AFailedSaveIsReportedAndTheBundleStaysOpen()
    {
        _tracker.IsDirty = true;
        _repository.SelectedFilePath = "/media/episode-01.mpvm";
        _windows.Choice = UnsavedChangesChoice.Save;
        _fileService.SaveError = new BundleSaveException("Could not write '/media/episode-01.mpvm'.");

        Assert.False(await CreateGuard().ConfirmProceedAsync("closing mpvmux"));
        Assert.Equal("Could not save the bundle: Could not write '/media/episode-01.mpvm'.",
            _dialogHelper.LastError);
    }

    // ---- fakes -----------------------------------------------------------------

    private sealed class FakeEditTracker : IBundleEditTracker
    {
        public event EventHandler? Edited { add { } remove { } }
        public event EventHandler? DirtyChanged { add { } remove { } }

        public bool IsDirty { get; set; }
        public int Revision => 0;
        public IDisposable Suspend() => new NoopDisposable();
        public void MarkSaved(int revision) { }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeAutoSave : IBundleAutoSaveService
    {
        public event EventHandler? Saved { add { } remove { } }
        public event EventHandler<string>? Failed { add { } remove { } }

        public bool IsEnabled => true;
        public bool FlushResult { get; set; }
        public int Flushes { get; private set; }

        public Task<bool> FlushAsync()
        {
            Flushes++;
            return Task.FromResult(FlushResult);
        }

        public void Cancel() { }
    }

    private sealed class FakeBundleFileService : IBundleFileService
    {
        public int Saves { get; private set; }
        public int SaveAs { get; private set; }
        public string? LastSaveAsPath { get; private set; }
        public Exception? SaveError { get; set; }

        public Task<BundleLoadResult> LoadAsync(string path) => throw new NotSupportedException();
        public Task<BundleLoadResult> RestoreBackupAsync(string path) => throw new NotSupportedException();
        public void RememberBundles(IReadOnlyList<string> paths) { }
        public void NewFile() { }

        public Task<BundleSaveResult> SaveAsAsync(string path)
        {
            SaveAs++;
            LastSaveAsPath = path;
            ThrowIfConfigured();
            return Task.FromResult(new BundleSaveResult());
        }

        public Task<BundleSaveResult> SaveAsync(BundleBackupMode backupMode = BundleBackupMode.Promote)
        {
            Saves++;
            ThrowIfConfigured();
            return Task.FromResult(new BundleSaveResult());
        }

        private void ThrowIfConfigured()
        {
            if (SaveError is not null)
                throw SaveError;
        }
    }

    private sealed class FakeBundleRepository : IBundleRepository
    {
        public string SelectedFilePath { get; set; } = "";
        public event EventHandler? SelectedFilePathChanged { add { } remove { } }

        public Task<BundleSaveResult> SaveAsync(
            MediaBundleModel bundle,
            BundleBackupMode backupMode = BundleBackupMode.Promote) => throw new NotSupportedException();

        public Task<BundleSaveResult> SaveAsAsync(MediaBundleModel bundle, string path) =>
            throw new NotSupportedException();

        public Task<BundleLoadResult> LoadFromFileAsync(string filePath) => throw new NotSupportedException();
        public Task<BundleLoadResult> RestoreBackupAsync(string filePath) => throw new NotSupportedException();
        public void ClearSelectedFile() { }
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public string? Path { get; set; }

        public Task<List<MediaRecord>?> GetFilesFromFolder(string[] extensions) => throw new NotSupportedException();
        public Task<string?> PickMediaFileAsync(string[] extensions) => throw new NotSupportedException();
        public Task<string?> OpenFilePickerAsync() => throw new NotSupportedException();
        public Task<string?> SaveFilePickerAsync() => Task.FromResult(Path);
        public Task<string?> PickExecutableAsync() => throw new NotSupportedException();
        public Task<string?> PickFolderAsync() => throw new NotSupportedException();
    }

    private sealed class FakeWindowService : IWindowService
    {
        public UnsavedChangesChoice Choice { get; set; } = UnsavedChangesChoice.Cancel;
        public string? LastPrompt { get; private set; }

        public void ShowMessage(string title, string text) { }
        public Task<bool> ShowConfirmAsync(string title, string text) => Task.FromResult(false);

        public Task<UnsavedChangesChoice> ShowUnsavedChangesAsync(string title, string text)
        {
            LastPrompt = text;
            return Task.FromResult(Choice);
        }

        public void ShowPreferences() { }
        public void ShowAbout() { }
        public void ShowUpdater(UpdateResponse result) { }
        public Task<string?> ShowRenameAsync(string currentName) => Task.FromResult<string?>(null);
        public void ShowRecentBundles() { }
        public Task<string?> ShowBundleChoiceAsync(IReadOnlyList<string> paths) =>
            Task.FromResult<string?>(null);
        public Task<IReadOnlyList<MediaRelinkSelection>?> ShowRelinkMediaAsync(
            IReadOnlyList<MediaRelinkProposal> proposals) =>
            Task.FromResult<IReadOnlyList<MediaRelinkSelection>?>(null);
        public Task ShowSetupAsync() => Task.CompletedTask;
    }

    private sealed class FakeDialogHelper : IDialogHelper
    {
        public string? LastError { get; private set; }

        public void ShowError(string text) => LastError = text;
        public void ShowInfo(string text) { }
    }
}
