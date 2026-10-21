using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// owns the history list, the quick-switch projection, and the load protocol
internal partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryService _historyService;
    private readonly IBundleRepository _bundleRepository;
    private readonly IBundleFileService _bundleFileService;
    private readonly IDialogHelper _dialogHelper;
    private readonly IWindowService _windowService;
    private readonly IConfigService _configService;

    private int _loadGeneration;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public HistoryViewModel(
        IHistoryService historyService,
        IBundleRepository bundleRepository,
        IBundleFileService bundleFileService,
        IDialogHelper dialogHelper,
        IWindowService windowService,
        IConfigService configService)
    {
        _historyService = historyService;
        _bundleRepository = bundleRepository;
        _bundleFileService = bundleFileService;
        _dialogHelper = dialogHelper;
        _windowService = windowService;
        _configService = configService;

        _historyService.OnHistoryChanged += OnHistoryChanged;
        _configService.Config.PropertyChanged += OnConfigPropertyChanged;

        RebuildEntries();
        IsHistoryExists = Entries.Count > 0;
    }

    /// <summary>Recent entries in display order: pinned bundles first, otherwise history order.</summary>
    [ObservableProperty]
    private ObservableCollection<MediaRecord> _entries = [];

    /// <summary>
    /// The capped projection shown by the top-bar dropdown: every pinned entry plus the most
    /// recent <see cref="ConfigModel.RecentLimit"/> unpinned entries.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MediaRecord> _quickEntries = [];

    /// <summary>Name shown on the dropdown button; falls back to the loaded file's name.</summary>
    [ObservableProperty]
    private string _currentBundleName = "Select a bundle";

    [ObservableProperty]
    private int _historyCount;

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
        var record = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

        CurrentBundleName = record?.Name
            ?? (string.IsNullOrEmpty(path) ? "Select a bundle" : Path.GetFileNameWithoutExtension(path));
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
    private async Task OpenRecord(MediaRecord? record)
    {
        if (record is null)
            return;

        await OpenAsync(record.Path);
    }

    [RelayCommand]
    private void TogglePin(MediaRecord record)
    {
        if (record == null) return;

        _historyService.TogglePin(record);
    }

    [RelayCommand]
    private void ManageRecentBundles() => _windowService.ShowRecentBundles();

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
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Preferences edits the limit on save; the dropdown should follow immediately
        if (e.PropertyName == nameof(ConfigModel.RecentLimit))
            RebuildEntries();
    }

    private void RebuildEntries()
    {
        // HistoryService maintains the display order: pinned prefix, then recent by use.
        Entries = new ObservableCollection<MediaRecord>(_historyService.HistoryModel.History);
        HistoryCount = Entries.Count;

        var pinnedCount = Entries.Count(entry => entry.IsPinned);
        var recentLimit = Math.Clamp(
            _configService.Config.RecentLimit,
            ConfigModel.MinRecentLimit,
            ConfigModel.MaxRecentLimit);

        QuickEntries = new ObservableCollection<MediaRecord>(
            Entries.Take(pinnedCount + recentLimit));

        SelectByPath(_bundleRepository.SelectedFilePath);
    }
}
