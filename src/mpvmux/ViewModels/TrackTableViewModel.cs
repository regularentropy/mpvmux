using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// <summary>The three columns of the track table, each a separate drop target.</summary>
internal enum TrackSection
{
    Video,
    Audio,
    Subtitle
}

/// <summary>
/// Owns the video/audio/subtitle grid. One row is one playable video with optional
/// audio and subtitle cells; the video is the row's spine.
/// </summary>
internal partial class TrackTableViewModel : ViewModelBase
{
    private readonly BundleSession _bundleSession;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogHelper _dialogHelper;
    private readonly HistoryViewModel _historyViewModel;

    private MediaBundleModel _bundle;
    private ObservableCollection<MediaRecord> _videoList;
    private ObservableCollection<MediaRecord> _audioList;
    private ObservableCollection<MediaRecord> _subtitleList;

    // the two directions of the SelectedRowIndex <-> SelectedVideoIndex sync
    private bool _syncingToBundle;
    private bool _syncingFromBundle;

    [ObservableProperty]
    private int _selectedRowIndex = -1;

    public TrackTableViewModel(
        BundleSession bundleSession,
        IFilePickerService filePickerService,
        IDialogHelper dialogHelper,
        HistoryViewModel historyViewModel)
    {
        _bundleSession = bundleSession;
        _filePickerService = filePickerService;
        _dialogHelper = dialogHelper;

        // a dropped bundle is opened through the history view: it owns the single load lock
        // and the backup-restore prompt, and registers the bundle in the recent list
        _historyViewModel = historyViewModel;

        _bundle = bundleSession.Bundle;
        _videoList = _bundle.VideoList;
        _audioList = _bundle.AudioList;
        _subtitleList = _bundle.SubtitleList;

        AttachBundle();
        _bundleSession.PropertyChanged += OnSessionPropertyChanged;

        RebuildRows();
        SyncSelectionFromBundle();
    }

    public ObservableCollection<TrackRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>
    /// The grid's selection. Selecting a row without a video clears the playable
    /// selection but keeps the row highlighted so the user can see the orphan.
    /// </summary>
    partial void OnSelectedRowIndexChanged(int value)
    {
        if (_syncingFromBundle)
            return;

        _syncingToBundle = true;
        try
        {
            _bundle.SelectedVideoIndex = RowHasVideo(value) ? value : -1;
        }
        finally
        {
            _syncingToBundle = false;
        }
    }

    [RelayCommand]
    private Task LoadVideo() => LoadFiles(AppConstants.VideoExtensions, _bundleSession.Bundle.SetVideoList);

    [RelayCommand]
    private Task LoadAudio() => LoadFiles(AppConstants.AudioExtensions, _bundleSession.Bundle.SetAudioList);

    [RelayCommand]
    private Task LoadSubtitle() => LoadFiles(AppConstants.SubtitleExtensions, _bundleSession.Bundle.SetSubtitleList);

    /// <summary>
    /// Loads what was dropped on a column, replacing that column's list like the
    /// "Load folder…" button does. Folders are scanned one level deep; dropped
    /// files are accepted when they match the column's supported extensions.
    /// </summary>
    internal async Task LoadDroppedAsync(TrackSection section, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        // a dropped bundle always opens that bundle, whichever column it landed on, and
        // joins the history like File > Open does
        if (BundleDropClassifier.FindBundle(paths) is { } bundlePath)
        {
            await _historyViewModel.OpenAsync(bundlePath);
            return;
        }

        var bundle = _bundleSession.Bundle;

        string[] extensions;
        Action<List<MediaRecord>> apply;
        switch (section)
        {
            case TrackSection.Video:
                extensions = AppConstants.VideoExtensions;
                apply = bundle.SetVideoList;
                break;
            case TrackSection.Audio:
                extensions = AppConstants.AudioExtensions;
                apply = bundle.SetAudioList;
                break;
            case TrackSection.Subtitle:
                extensions = AppConstants.SubtitleExtensions;
                apply = bundle.SetSubtitleList;
                break;
            default:
                return;
        }

        await LoadFiles(
            extensions,
            apply,
            load: () => MediaFileScanner.Collect(paths, extensions),
            emptyMessage: "No supported media files were found in the dropped items.");
    }

    [RelayCommand]
    private void ClearVideo()
    {
        _bundle.VideoList.Clear();
        _bundle.SelectedVideoIndex = -1;
    }

    [RelayCommand]
    private void ClearAudio() => _bundle.AudioList.Clear();

    [RelayCommand]
    private void ClearSubtitles() => _bundle.SubtitleList.Clear();

    [RelayCommand]
    private void RemoveVideo(TrackRow? row)
    {
        if (row is null || !RemoveAt(_bundle.VideoList, row.Index))
            return;

        // the row is the triple: drop its optional cells too so the remaining rows
        // keep their alignment instead of sliding one row up
        RemoveAt(_bundle.AudioList, row.Index);
        RemoveAt(_bundle.SubtitleList, row.Index);

        _bundle.SelectedVideoIndex = _bundle.VideoList.Count == 0
            ? -1
            : Math.Min(row.Index, _bundle.VideoList.Count - 1);
    }

    [RelayCommand]
    private void RemoveAudio(TrackRow? row) => ClearCell(_bundle.AudioList, row);

    [RelayCommand]
    private void RemoveSubtitle(TrackRow? row) => ClearCell(_bundle.SubtitleList, row);

    [RelayCommand]
    private Task ReplaceVideo(TrackRow? row) =>
        ReplaceAsync(row, AppConstants.VideoExtensions, _bundle.VideoList, selectRow: true);

    [RelayCommand]
    private Task ReplaceAudio(TrackRow? row) =>
        ReplaceAsync(row, AppConstants.AudioExtensions, _bundle.AudioList, selectRow: false);

    [RelayCommand]
    private Task ReplaceSubtitle(TrackRow? row) =>
        ReplaceAsync(row, AppConstants.SubtitleExtensions, _bundle.SubtitleList, selectRow: false);

    private async Task LoadFiles(
        string[] extensions,
        Action<List<MediaRecord>> apply,
        Func<List<MediaRecord>>? load = null,
        string emptyMessage = "No supported media files were found in that folder.")
    {
        List<MediaRecord>? files;
        try
        {
            files = load is null
                ? await _filePickerService.GetFilesFromFolder(extensions)
                : await Task.Run(load);
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Could not read the folder: {ex.Message}");
            return;
        }

        if (files is null)
            return;

        if (files.Count == 0)
        {
            // replacing the list with nothing would silently wipe what is already loaded
            _dialogHelper.ShowInfo(emptyMessage);
            return;
        }

        apply(files);
    }

    private async Task ReplaceAsync(
        TrackRow? row,
        string[] extensions,
        ObservableCollection<MediaRecord> list,
        bool selectRow)
    {
        if (row is null)
            return;

        var path = await _filePickerService.PickMediaFileAsync(extensions);
        if (string.IsNullOrWhiteSpace(path))
            return;

        SetAt(list, row.Index, new MediaRecord(Path.GetFileNameWithoutExtension(path), path));

        if (selectRow)
            _bundle.SelectedVideoIndex = row.Index;
    }

    private static void SetAt(ObservableCollection<MediaRecord> list, int index, MediaRecord record)
    {
        // keep the list aligned with the row even when the user fills a later cell first
        while (list.Count < index)
            list.Add(MediaRecord.Placeholder);

        if (list.Count == index)
            list.Add(record);
        else
            list[index] = record;
    }

    private static bool RemoveAt(ObservableCollection<MediaRecord> list, int index)
    {
        if (index < 0 || index >= list.Count)
            return false;

        list.RemoveAt(index);
        return true;
    }

    private static void ClearCell(ObservableCollection<MediaRecord> list, TrackRow? row)
    {
        if (row is null || row.Index < 0 || row.Index >= list.Count)
            return;

        list[row.Index] = MediaRecord.Placeholder;
    }

    private void AttachBundle()
    {
        _bundle.PropertyChanged += OnBundlePropertyChanged;
        AttachLists();
    }

    private void DetachBundle()
    {
        _bundle.PropertyChanged -= OnBundlePropertyChanged;
        DetachLists();
    }

    private void AttachLists()
    {
        _videoList.CollectionChanged += OnListChanged;
        _audioList.CollectionChanged += OnListChanged;
        _subtitleList.CollectionChanged += OnListChanged;
    }

    private void DetachLists()
    {
        _videoList.CollectionChanged -= OnListChanged;
        _audioList.CollectionChanged -= OnListChanged;
        _subtitleList.CollectionChanged -= OnListChanged;
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRows();

    private void OnBundlePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaBundleModel.VideoList):
            case nameof(MediaBundleModel.AudioList):
            case nameof(MediaBundleModel.SubtitleList):
                // deserialization replaces the collections, so the old subscriptions are dead
                DetachLists();
                _videoList = _bundle.VideoList;
                _audioList = _bundle.AudioList;
                _subtitleList = _bundle.SubtitleList;
                AttachLists();
                RebuildRows();
                break;
            case nameof(MediaBundleModel.SelectedVideoIndex):
                if (!_syncingToBundle)
                    SyncSelectionFromBundle();
                break;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BundleSession.Bundle))
            return;

        DetachBundle();
        _bundle = _bundleSession.Bundle;
        _videoList = _bundle.VideoList;
        _audioList = _bundle.AudioList;
        _subtitleList = _bundle.SubtitleList;
        AttachBundle();
        RebuildRows();
        SyncSelectionFromBundle();
    }

    private void RebuildRows()
    {
        var count = Math.Max(_videoList.Count, Math.Max(_audioList.Count, _subtitleList.Count));
        var previous = SelectedRowIndex;

        _syncingFromBundle = true;
        try
        {
            while (Rows.Count > count)
                Rows.RemoveAt(Rows.Count - 1);

            while (Rows.Count < count)
                Rows.Add(CreateRow(Rows.Count));

            for (var i = 0; i < count; i++)
            {
                var row = CreateRow(i);
                if (!Rows[i].HasSameData(row))
                    Rows[i] = row;
            }

            // keep an orphan row selected while lists change under it
            var next = _bundle.SelectedVideoIndex >= 0 ? _bundle.SelectedVideoIndex : previous;
            if (next >= count)
                next = count - 1;
            if (next < -1)
                next = -1;
            SelectedRowIndex = next;
        }
        finally
        {
            _syncingFromBundle = false;
        }

        OnPropertyChanged(nameof(HasRows));
    }

    private void SyncSelectionFromBundle()
    {
        _syncingFromBundle = true;
        try
        {
            var index = _bundle.SelectedVideoIndex;
            SelectedRowIndex = index >= 0 && index < Rows.Count ? index : -1;
        }
        finally
        {
            _syncingFromBundle = false;
        }
    }

    private TrackRow CreateRow(int index) => new(
        this,
        index,
        TrackAt(_videoList, index),
        TrackAt(_audioList, index),
        TrackAt(_subtitleList, index));

    private static MediaRecord? TrackAt(ObservableCollection<MediaRecord> list, int index) =>
        index >= 0 && index < list.Count && !string.IsNullOrWhiteSpace(list[index].Path)
            ? list[index]
            : null;

    private bool RowHasVideo(int index) =>
        index >= 0 && index < _videoList.Count && !string.IsNullOrWhiteSpace(_videoList[index].Path);
}
