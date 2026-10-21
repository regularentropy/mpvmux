using CommunityToolkit.Mvvm.Input;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class BottomPanelViewModel : ViewModelBase
{
    private readonly BundleSession _bundleSession;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;

    private MediaBundleModel _bundle;
    private ObservableCollection<MediaRecord> _videoList;
    private ObservableCollection<MediaRecord> _audioList;

    public BottomPanelViewModel(
        BundleSession bundleSession,
        IFilePickerService filePickerService,
        IPlayerService playerService, 
        IDialogHelper dialogHelper)
    {
        _bundleSession = bundleSession;
        _filePickerService = filePickerService;
        _playerService = playerService;
        _dialogHelper = dialogHelper;

        _bundle = bundleSession.Bundle;
        _videoList = _bundle.VideoList;
        _audioList = _bundle.AudioList;
        AttachBundle();

        _bundleSession.PropertyChanged += OnSessionPropertyChanged;
        _playerService.IsPlayingChanged += OnIsPlayingChanged;
    }

    public bool IsPlaying => _playerService.IsPlaying;

    public string PlaybackPosition
    {
        get
        {
            var bundle = _bundleSession.Bundle;
            return bundle.SelectedVideoIndex >= 0 && bundle.SelectedVideoIndex < bundle.VideoList.Count
                ? $"{bundle.SelectedVideoIndex + 1} / {bundle.VideoList.Count}"
                : "";
        }
    }

    public string PlaybackHint
    {
        get
        {
            if (_playerService.IsPlaying)
                return "Playing in mpv";

            var bundle = _bundleSession.Bundle;
            var videos = bundle.VideoList.Count;
            var audio = bundle.AudioList.Count;

            if (videos == 0 && audio == 0)
                return "Add a video and an audio track to begin.";
            if (videos == 0)
                return "Add a video to begin.";
            if (audio == 0)
                return "Add an audio track to begin.";
            if (bundle.SelectedVideoIndex < 0)
                return "Select a video.";
            if (bundle.SelectedAudioIndex < 0)
                return "Select an audio track.";

            return "";
        }
    }

    private void AttachBundle()
    {
        _bundle.PropertyChanged += OnBundlePropertyChanged;
        _videoList.CollectionChanged += OnListChanged;
        _audioList.CollectionChanged += OnListChanged;
    }

    private void DetachBundle()
    {
        _bundle.PropertyChanged -= OnBundlePropertyChanged;
        _videoList.CollectionChanged -= OnListChanged;
        _audioList.CollectionChanged -= OnListChanged;
    }

    private void OnBundlePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaBundleModel.VideoList):
            case nameof(MediaBundleModel.AudioList):
                // deserialization replaces the collections, so the old subscriptions are dead
                DetachBundle();
                _videoList = _bundle.VideoList;
                _audioList = _bundle.AudioList;
                AttachBundle();
                RefreshState();
                break;
            case nameof(MediaBundleModel.SelectedVideoIndex):
            case nameof(MediaBundleModel.SelectedAudioIndex):
                RefreshState();
                break;
        }
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshState();

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BundleSession.Bundle))
            return;

        DetachBundle();
        _bundle = _bundleSession.Bundle;
        _videoList = _bundle.VideoList;
        _audioList = _bundle.AudioList;
        AttachBundle();
        RefreshState();
    }

    private void OnIsPlayingChanged(object? sender, EventArgs e) => RefreshState();

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlaybackPosition));
        OnPropertyChanged(nameof(PlaybackHint));
        LaunchMPVCommand.NotifyCanExecuteChanged();
    }

    private bool CanPlay() =>
        !_playerService.IsPlaying && _bundleSession.Bundle.TryGetSelectedPaths(out _, out _);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void LaunchMPV()
    {
        var bundle = _bundleSession.Bundle;
        if (!bundle.TryGetSelectedPaths(out _, out _))
            return;

        try
        {
            _playerService.LaunchMPV(bundle);
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to launch MPV: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Stop() => _playerService.Stop();

    [RelayCommand]
    private Task LoadVideo() => LoadFiles(AppConstants.VideoExtensions, _bundleSession.Bundle.SetVideoList);

    [RelayCommand]
    private Task LoadAudio() => LoadFiles(AppConstants.AudioExtensions, _bundleSession.Bundle.SetAudioList);

    private async Task LoadFiles(string[] extensions, Action<List<MediaRecord>> apply)
    {
        List<MediaRecord>? files;
        try
        {
            files = await _filePickerService.GetFilesFromFolder(extensions);
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to read the folder: {ex.Message}");
            return;
        }

        if (files == null) return;

        if (files.Count == 0)
        {
            // replacing the list with nothing would silently wipe what is already loaded
            _dialogHelper.ShowInfo("No supported media files found in that folder.");
            return;
        }

        apply(files);
    }

    [RelayCommand]
    private void ClearVideo()
    {
        _bundleSession.Bundle.VideoList.Clear();
    }

    [RelayCommand]
    private void ClearAudio()
    {
        _bundleSession.Bundle.AudioList.Clear();
    }

    [RelayCommand]
    private void RemoveVideo(MediaRecord? record)
    {
        var bundle = _bundleSession.Bundle;
        bundle.SelectedVideoIndex = RemoveRecord(bundle.VideoList, record, bundle.SelectedVideoIndex);
    }

    [RelayCommand]
    private void RemoveAudio(MediaRecord? record)
    {
        var bundle = _bundleSession.Bundle;
        bundle.SelectedAudioIndex = RemoveRecord(bundle.AudioList, record, bundle.SelectedAudioIndex);
    }

    private static int RemoveRecord(
        ObservableCollection<MediaRecord> list,
        MediaRecord? record,
        int selectedIndex)
    {
        if (record is null)
            return selectedIndex;

        var index = list.IndexOf(record);
        if (index < 0)
            return selectedIndex;

        list.RemoveAt(index);

        if (list.Count == 0)
            return -1;
        if (index < selectedIndex)
            return selectedIndex - 1;
        if (index == selectedIndex && selectedIndex >= list.Count)
            return list.Count - 1;

        return selectedIndex;
    }
}
