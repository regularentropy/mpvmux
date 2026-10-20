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
    private readonly BundleSession _bundleSession;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;

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
        _playerService.IsPlayingChanged += OnIsPlayingChanged;
    }

    private void OnIsPlayingChanged(object? sender, EventArgs e)
    {
        LaunchMPVCommand.NotifyCanExecuteChanged();
    }

    private bool CanPlay() => !_playerService.IsPlaying;

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
}
