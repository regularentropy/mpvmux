using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// owns the history list, its selection, and the load protocol the selection drives
internal partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryService _historyService;
    private readonly IBundleRepository _bundleRepository;
    private readonly IBundleFileService _bundleFileService;
    private readonly IDialogHelper _dialogHelper;
    private readonly IWindowService _windowService;

    private int _loadGeneration;
    private bool _suppressSelection;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public HistoryViewModel(
        IHistoryService historyService,
        IBundleRepository bundleRepository,
        IBundleFileService bundleFileService,
        IDialogHelper dialogHelper,
        IWindowService windowService)
    {
        _historyService = historyService;
        _bundleRepository = bundleRepository;
        _bundleFileService = bundleFileService;
        _dialogHelper = dialogHelper;
        _windowService = windowService;

        _historyService.OnHistoryChanged += OnHistoryChanged;

        RebuildEntries();
        IsHistoryExists = Entries.Count > 0;
    }

    /// <summary>Recent entries in display order: pinned bundles first, otherwise history order.</summary>
    [ObservableProperty]
    private ObservableCollection<MediaRecord> _entries = [];

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
    private void TogglePin(MediaRecord record)
    {
        if (record == null) return;

        _historyService.TogglePin(record);
    }

    [RelayCommand]
    private async Task RenameHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        var name = await _windowService.ShowRenameAsync(record.Name);
        if (name == null) return;

        _historyService.RenameEntry(record, name);
    }

    [RelayCommand]
    private async Task DeleteHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        var indexToDelete = Entries.IndexOf(record);
        var wasLoaded = string.Equals(_bundleRepository.SelectedFilePath, record.Path, StringComparison.OrdinalIgnoreCase);

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
        RebuildEntries();
        IsHistoryExists = Entries.Count > 0;
        SelectByPath(_bundleRepository.SelectedFilePath);
    }

    private void RebuildEntries()
    {
        // OrderByDescending is stable, so each group keeps the order it has in history.json
        Entries = new ObservableCollection<MediaRecord>(
            _historyService.HistoryModel.History.OrderByDescending(entry => entry.IsPinned));
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
