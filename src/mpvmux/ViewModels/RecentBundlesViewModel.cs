using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly IFilePickerService _filePickerService;
    private readonly IBundleRelinkService _relinkService;
    private readonly IWindowService _windowService;
    private readonly IDialogHelper _dialogHelper;
    private readonly DispatcherTimer _filterTimer;

    public RecentBundlesViewModel(
        IHistoryService historyService,
        HistoryViewModel historyViewModel,
        IFilePickerService filePickerService,
        IBundleRelinkService relinkService,
        IWindowService windowService,
        IDialogHelper dialogHelper)
    {
        _historyService = historyService;
        _historyViewModel = historyViewModel;
        _filePickerService = filePickerService;
        _relinkService = relinkService;
        _windowService = windowService;
        _dialogHelper = dialogHelper;

        // the window is created on demand, so this picks up files deleted since app start
        _historyService.RefreshAvailability();

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

    /// <summary>True while the bulk re-link scan runs, so the UI can disable the buttons.</summary>
    [ObservableProperty]
    private bool _isRelinking;

    [ObservableProperty]
    private int _obsoleteCount;

    [ObservableProperty]
    private bool _hasObsolete;

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
        var obsolete = all.Count(record => record.IsMissing);

        PinnedEntries = new ObservableCollection<MediaRecord>(pinned);
        RecentEntries = new ObservableCollection<MediaRecord>(recent);

        HasPinned = pinned.Count > 0;
        HasRecent = recent.Count > 0;
        IsFiltered = query.Length > 0;
        IsEmpty = all.Count == 0;
        IsNoMatch = !IsEmpty && pinned.Count + recent.Count == 0;
        ObsoleteCount = obsolete;
        HasObsolete = obsolete > 0;

        var summary = IsEmpty
            ? "No recent bundles"
            : IsFiltered
                ? $"{pinned.Count + recent.Count} of {all.Count} bundles"
                : $"{pinnedTotal} pinned · {all.Count - pinnedTotal} recent";

        if (!IsEmpty && obsolete > 0)
            summary += $" · {obsolete} obsolete";

        Summary = summary;

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

        // a missing bundle cannot be loaded; offer to re-link it instead
        if (record.IsMissing)
        {
            var linked = await RelinkRecordAsync(record);
            if (linked is null)
                return;

            await _historyViewModel.OpenAsync(linked);
        }
        else
        {
            await _historyViewModel.OpenAsync(record.Path);
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task Relink(MediaRecord? record)
    {
        if (record is null)
            return;

        await RelinkRecordAsync(record);
    }

    /// <summary>
    /// Asks for the bundle file and re-points <paramref name="record"/> at it. Completes with
    /// the new path, or null when the user cancels or picks an invalid file.
    /// </summary>
    private async Task<string?> RelinkRecordAsync(MediaRecord record)
    {
        var path = await _filePickerService.OpenFilePickerAsync();
        if (path is null)
            return null;

        if (!File.Exists(path)
            || !string.Equals(Path.GetExtension(path), AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            _dialogHelper.ShowError("Please choose an existing .mpvm bundle file.");
            return null;
        }

        return _historyService.RelinkEntry(record, path) ? path : null;
    }

    [RelayCommand]
    private async Task RelinkObsolete()
    {
        var missing = _historyService.HistoryModel.History
            .Where(record => record.IsMissing)
            .ToList();

        if (missing.Count == 0)
            return;

        var folder = await _filePickerService.PickFolderAsync();
        if (folder is null)
            return;

        var names = missing
            .Select(record => Path.GetFileName(record.Path))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IsRelinking = true;
        try
        {
            var matches = await _relinkService.FindByNameAsync(folder, names);

            var relinked = 0;
            var ambiguous = 0;
            var unmatched = 0;

            foreach (var record in missing)
            {
                var name = Path.GetFileName(record.Path);

                if (!matches.TryGetValue(name, out var paths) || paths.Count == 0)
                {
                    unmatched++;
                    continue;
                }

                if (paths.Count > 1)
                {
                    ambiguous++;
                    continue;
                }

                if (_historyService.RelinkEntry(record, paths[0]))
                    relinked++;
                else
                    unmatched++;
            }

            var unresolved = ambiguous + unmatched;
            _dialogHelper.ShowInfo(unresolved == 0
                ? $"Re-linked {relinked} bundle(s)."
                : $"Re-linked {relinked} bundle(s). {unresolved} still need attention ({ambiguous} ambiguous, {unmatched} not found).");
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to scan the folder: {ex.Message}");
        }
        finally
        {
            IsRelinking = false;
        }
    }

    [RelayCommand]
    private async Task RemoveObsolete()
    {
        // do not offer to delete something that came back while the window was open
        _historyService.RefreshAvailability();

        var count = _historyService.HistoryModel.History.Count(record => record.IsMissing);
        if (count == 0)
            return;

        var label = count == 1 ? "1 obsolete entry" : $"{count} obsolete entries";
        if (!await _windowService.ShowConfirmAsync(
                "Remove obsolete entries",
                $"Remove {label} from history? This cannot be undone."))
        {
            return;
        }

        _historyService.RemoveObsoleteEntries();
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
