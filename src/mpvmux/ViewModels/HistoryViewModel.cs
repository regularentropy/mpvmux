using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// owns the history list, its selection, and the load protocol the selection drives
internal partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryService _historyService;
    private readonly IVideoRepositoryService _videoRepositoryService;
    private readonly IBundleFileService _bundleFileService;
    private readonly IDialogHelper _dialogHelper;

    private int _loadGeneration;
    private bool _suppressSelection;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public HistoryViewModel(
        IHistoryService historyService,
        IVideoRepositoryService videoRepositoryService,
        IBundleFileService bundleFileService,
        IDialogHelper dialogHelper)
    {
        _historyService = historyService;
        _videoRepositoryService = videoRepositoryService;
        _bundleFileService = bundleFileService;
        _dialogHelper = dialogHelper;

        _historyService.OnHistoryChanged += OnHistoryChanged;

        IsHistoryExists = Entries.Count > 0;
    }

    public ObservableCollection<MediaRecord> Entries => _historyService.HistoryModel.History;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isHistoryExists;

    /// loads a path and points the selection at it; used by the file commands and at startup
    public async Task OpenAsync(string path)
    {
        if (await LoadAsync(path))
            SelectByPath(path);
    }

    public void SelectByPath(string path)
    {
        _suppressSelection = true;
        try { SelectedIndex = IndexOfPath(path); }
        finally { _suppressSelection = false; }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_suppressSelection) return;
        if (value < 0 || value >= Entries.Count) return;

        _ = LoadAsync(Entries[value].Path);
    }

    /// one load at a time; a superseded load drops its result instead of applying it
    private async Task<bool> LoadAsync(string path)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);

        await _loadLock.WaitAsync();
        try
        {
            if (generation != Volatile.Read(ref _loadGeneration)) return false;

            await _bundleFileService.LoadAsync(path);
            return true;
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _loadGeneration))
                _dialogHelper.ShowError($"Failed to load file: {ex.Message}");

            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    [RelayCommand]
    private async Task DeleteHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        var indexToDelete = Entries.IndexOf(record);
        var wasLoaded = string.Equals(_videoRepositoryService.SelectedFilePath, record.Path, StringComparison.OrdinalIgnoreCase);

        // RemoveEntry raises OnHistoryChanged, which re-points the selection at the loaded file
        _historyService.RemoveEntry(record);

        if (!wasLoaded) return;

        if (Entries.Count == 0)
        {
            _bundleFileService.NewFile();
            return;
        }

        var next = Math.Min(indexToDelete, Entries.Count - 1);
        SelectByPath(Entries[next].Path);
        await LoadAsync(Entries[next].Path);
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        IsHistoryExists = Entries.Count > 0;
        SelectByPath(_videoRepositoryService.SelectedFilePath);
    }

    private int IndexOfPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return -1;

        for (var i = 0; i < Entries.Count; i++)
        {
            if (string.Equals(Entries[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
