using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class BottomPanelViewModel : ViewModelBase
{
    private readonly BundleSession _bundleSession;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;
    private readonly IConfigService _configService;

    private MediaBundleModel _bundle;
    private ObservableCollection<MediaRecord> _videoList;

    public BottomPanelViewModel(
        BundleSession bundleSession,
        IPlayerService playerService,
        IDialogHelper dialogHelper,
        IConfigService configService)
    {
        _bundleSession = bundleSession;
        _playerService = playerService;
        _dialogHelper = dialogHelper;
        _configService = configService;

        _bundle = bundleSession.Bundle;
        _videoList = _bundle.VideoList;
        AttachBundle();

        _bundleSession.PropertyChanged += OnSessionPropertyChanged;
        _playerService.IsPlayingChanged += OnIsPlayingChanged;
        _configService.Config.PropertyChanged += OnConfigPropertyChanged;
    }

    public bool IsPlaying => _playerService.IsPlaying;

    /// <summary>Whether playback advances to the next video when mpv exits.</summary>
    public bool IsAutoplayEnabled => _configService.Config.EnableAutomaticPlayback;

    /// <summary>Whether playback starts in fullscreen.</summary>
    public bool IsFullscreenEnabled => _configService.Config.EnableFullscreen;

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
                return "Drag a video folder here or use Load folder to begin.";
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

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Preferences, the Playback menu, tray and shortcuts all edit the config directly
        switch (e.PropertyName)
        {
            case nameof(ConfigModel.EnableAutomaticPlayback):
                OnPropertyChanged(nameof(IsAutoplayEnabled));
                break;
            case nameof(ConfigModel.EnableFullscreen):
                OnPropertyChanged(nameof(IsFullscreenEnabled));
                break;
        }
    }

    private void OnIsPlayingChanged(object? sender, EventArgs e) => RefreshState();

    private void RefreshState()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlaybackPosition));
        OnPropertyChanged(nameof(PlaybackHint));
        LaunchMPVCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        PreviousTrackCommand.NotifyCanExecuteChanged();
        NextTrackCommand.NotifyCanExecuteChanged();
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
            _dialogHelper.ShowError(DescribeLaunchError(ex));
        }
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private async Task Pause()
    {
        try
        {
            await _playerService.PauseAsync();
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Could not pause mpv: {ex.Message}");
        }
    }

    private bool CanGoPrevious() => _bundleSession.Bundle.SelectedVideoIndex > 0;

    private bool CanGoNext()
    {
        var bundle = _bundleSession.Bundle;
        return bundle.SelectedVideoIndex < bundle.VideoList.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousTrack() => SkipAsync(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextTrack() => SkipAsync(1);

    /// <summary>
    /// Moves the queue selection by <paramref name="offset"/>; when something is playing, the
    /// current mpv instance is replaced by the newly selected row.
    /// </summary>
    private async Task SkipAsync(int offset)
    {
        var bundle = _bundleSession.Bundle;
        var index = bundle.SelectedVideoIndex + offset;

        if (index < 0 || index >= bundle.VideoList.Count)
            return;

        bundle.SelectedVideoIndex = index;

        if (!_playerService.IsPlaying || !bundle.TryGetSelectedTracks(out _, out _, out _))
            return;

        try
        {
            await _playerService.StopAsync();
            _playerService.LaunchMPV(bundle);
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError(DescribeLaunchError(ex));
        }
    }

    private static string DescribeLaunchError(Exception ex)
    {
        var message = $"Could not start mpv: {ex.Message}";

        // a missing or unreadable executable is the common case; point at the remedy
        return ex is Win32Exception or InvalidOperationException
            ? $"{message} Choose the mpv location with File > Set up mpv."
            : message;
    }

    [RelayCommand]
    private void Stop() => _playerService.Stop();
}
