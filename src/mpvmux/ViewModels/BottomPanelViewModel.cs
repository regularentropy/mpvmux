using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace mpvmux.ViewModels;

internal partial class BottomPanelViewModel : ViewModelBase
{
    private readonly BundleSession _bundleSession;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;

    private MediaBundleModel _bundle;
    private ObservableCollection<MediaRecord> _videoList;

    public BottomPanelViewModel(
        BundleSession bundleSession,
        IPlayerService playerService,
        IDialogHelper dialogHelper)
    {
        _bundleSession = bundleSession;
        _playerService = playerService;
        _dialogHelper = dialogHelper;

        _bundle = bundleSession.Bundle;
        _videoList = _bundle.VideoList;
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

            // audio and subtitles are optional cells in the table, so only the video
            // row can block playback
            if (bundle.VideoList.Count == 0)
                return "Load a video folder to begin.";
            if (bundle.SelectedVideoIndex < 0)
                return "Select a video.";

            return "";
        }
    }

    private void AttachBundle()
    {
        _bundle.PropertyChanged += OnBundlePropertyChanged;
        _videoList.CollectionChanged += OnVideoListChanged;
    }

    private void DetachBundle()
    {
        _bundle.PropertyChanged -= OnBundlePropertyChanged;
        _videoList.CollectionChanged -= OnVideoListChanged;
    }

    private void OnVideoListChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshState();

    private void OnBundlePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaBundleModel.VideoList):
                // deserialization replaces the collection, so the old subscription is dead
                _videoList.CollectionChanged -= OnVideoListChanged;
                _videoList = _bundle.VideoList;
                _videoList.CollectionChanged += OnVideoListChanged;
                RefreshState();
                break;
            case nameof(MediaBundleModel.SelectedVideoIndex):
                RefreshState();
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
        !_playerService.IsPlaying && _bundleSession.Bundle.TryGetSelectedTracks(out _, out _, out _);

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void LaunchMPV()
    {
        var bundle = _bundleSession.Bundle;
        if (!bundle.TryGetSelectedTracks(out _, out _, out _))
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
}
