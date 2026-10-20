using CommunityToolkit.Mvvm.Input;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class BottomPanelViewModel : ViewModelBase
{
    private readonly MediaContext _mediaContext;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;
    private bool _isPlaying;

    public BottomPanelViewModel(
        MediaContext mc,
        IFilePickerService fps,
        IPlayerService playerService, 
        IDialogHelper dialogHelper)
    {
        _mediaContext = mc;
        _filePickerService = fps;
        _playerService = playerService;
        _dialogHelper = dialogHelper;
        _playerService.PlaybackEnded += OnPlaybackEnded;
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        _isPlaying = false;
        LaunchMPVCommand.NotifyCanExecuteChanged();
    }

    private bool CanPlay() => !_isPlaying;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void LaunchMPV()
    {
        var bundle = _mediaContext.Bundle;
        if (!bundle.TryGetSelectedPaths(out _, out _))
            return;

        _isPlaying = true;
        LaunchMPVCommand.NotifyCanExecuteChanged();

        try
        {
            _playerService.LaunchMPV(bundle);
        }
        catch (Exception ex)
        {
            _isPlaying = false;
            LaunchMPVCommand.NotifyCanExecuteChanged();
            _dialogHelper.ShowError($"Failed to launch MPV: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task LoadVideo() => LoadFiles(AppConstants.VideoExtensions, _mediaContext.Bundle.SetVideoList);

    [RelayCommand]
    private Task LoadAudio() => LoadFiles(AppConstants.AudioExtensions, _mediaContext.Bundle.SetAudioList);

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
        _mediaContext.Bundle.VideoList.Clear();

    }

    [RelayCommand]
    private void ClearAudio()
    {
        _mediaContext.Bundle.AudioList.Clear();
    }
}
