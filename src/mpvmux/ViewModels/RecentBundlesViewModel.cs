using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// <summary>
/// Backs the Manage recent bundles window. It shows the whole history in two sections and
/// allows manual ordering only inside the pinned group; recent entries stay most-recently-used.
/// </summary>
internal partial class RecentBundlesViewModel : ViewModelBase
{
    private readonly IHistoryService _historyService;
    private readonly HistoryViewModel _historyViewModel;
    private readonly DispatcherTimer _filterTimer;

    public RecentBundlesViewModel(IHistoryService historyService, HistoryViewModel historyViewModel)
    {
        _historyService = historyService;
        _historyViewModel = historyViewModel;

        // large histories would otherwise rebuild the filtered lists on every keystroke
        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            Rebuild();
        };

        _historyService.OnHistoryChanged += OnHistoryChanged;
        Rebuild();
    }

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private ObservableCollection<MediaRecord> _pinnedEntries = [];

    [ObservableProperty]
    private ObservableCollection<MediaRecord> _recentEntries = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private MediaRecord? _selectedPinned;

    [ObservableProperty]
    private MediaRecord? _selectedRecent;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private bool _hasPinned;

    [ObservableProperty]
    private bool _hasRecent;

    [ObservableProperty]
    private bool _isFiltered;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isNoMatch;

    partial void OnSearchTextChanged(string value)
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        var query = SearchText.Trim();
        var all = _historyService.HistoryModel.History;

        var pinned = new List<MediaRecord>();
        var recent = new List<MediaRecord>();

        foreach (var record in all)
        {
            if (!Matches(record, query))
                continue;

            if (record.IsPinned)
                pinned.Add(record);
            else
                recent.Add(record);
        }

        var pinnedTotal = all.Count(record => record.IsPinned);

        PinnedEntries = new ObservableCollection<MediaRecord>(pinned);
        RecentEntries = new ObservableCollection<MediaRecord>(recent);

        HasPinned = pinned.Count > 0;
        HasRecent = recent.Count > 0;
        IsFiltered = query.Length > 0;
        IsEmpty = all.Count == 0;
        IsNoMatch = !IsEmpty && pinned.Count + recent.Count == 0;

        Summary = IsEmpty
            ? "No recent bundles"
            : IsFiltered
                ? $"{pinned.Count + recent.Count} of {all.Count} bundles"
                : $"{pinnedTotal} pinned · {all.Count - pinnedTotal} recent";

        // keep the selection on the same record when it is still visible
        SelectedPinned = pinned.FirstOrDefault(record => SamePath(record, SelectedPinned?.Path));
        SelectedRecent = recent.FirstOrDefault(record => SamePath(record, SelectedRecent?.Path));
    }

    private static bool Matches(MediaRecord record, string query)
    {
        if (query.Length == 0)
            return true;

        return record.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || record.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(MediaRecord record, string? path) =>
        path is not null && string.Equals(record.Path, path, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task Open(MediaRecord? record)
    {
        if (record is null)
            return;

        await _historyViewModel.OpenAsync(record.Path);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void TogglePin(MediaRecord? record)
    {
        if (record is null)
            return;

        _historyService.TogglePin(record);
    }

    [RelayCommand]
    private async Task Rename(MediaRecord? record)
    {
        if (record is null)
            return;

        await _historyViewModel.RenameHistoryRecordCommand.ExecuteAsync(record);
    }

    [RelayCommand]
    private async Task Remove(MediaRecord? record)
    {
        if (record is null)
            return;

        await _historyViewModel.DeleteHistoryRecordCommand.ExecuteAsync(record);
    }

    [RelayCommand]
    private void MoveUp(MediaRecord? record) => MoveBy(record, -1);

    [RelayCommand]
    private void MoveDown(MediaRecord? record) => MoveBy(record, 1);

    [RelayCommand]
    private void MoveToTop(MediaRecord? record)
    {
        if (record is null)
            return;

        _historyService.PromoteEntry(record);
    }

    /// <summary>
    /// Moves a pinned record one visible slot up or down. While a filter is active this uses
    /// the filtered list, so the record lands next to the neighbour the user can actually see.
    /// </summary>
    private void MoveBy(MediaRecord? record, int offset)
    {
        if (record is null || !record.IsPinned)
            return;

        var index = IndexOfPath(PinnedEntries, record.Path);
        var targetIndex = index + offset;

        if (index < 0 || targetIndex < 0 || targetIndex >= PinnedEntries.Count)
            return;

        _historyService.MoveEntry(record, PinnedEntries[targetIndex], placeAfter: offset > 0);
    }

    /// <summary>Drop handler entry point for the pinned list.</summary>
    public bool MovePinned(MediaRecord record, MediaRecord target, bool placeAfter)
    {
        if (!record.IsPinned || !target.IsPinned || SamePath(record, target.Path))
            return false;

        return _historyService.MoveEntry(record, target, placeAfter);
    }

    private static int IndexOfPath(IReadOnlyList<MediaRecord> records, string path)
    {
        for (var i = 0; i < records.Count; i++)
        {
            if (string.Equals(records[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public void Dispose()
    {
        _filterTimer.Stop();
        _historyService.OnHistoryChanged -= OnHistoryChanged;
    }
}
