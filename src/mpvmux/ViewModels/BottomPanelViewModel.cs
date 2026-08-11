using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class BottomPanelViewModel : ViewModelBase
{
    private readonly MediaContext _mediaContext;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlayerService _playerService;
    private readonly IDialogHelper _dialogHelper;

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
    }

    [RelayCommand]
    private async Task LaunchMPV()
    {
        var selectedRecord = _mediaContext.Bundle.SelectedRecord;

        try
        {
            await Task.Run(() => _playerService.LaunchMPV(selectedRecord.Name, selectedRecord.Path));
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to launch MPV: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadVideo()
    {
        var a = await _filePickerService.GetFilesFromFolder();

        if (a == null) return;

        foreach (var item in a)
        {
            Debug.WriteLine(item);
        }

        _mediaContext.Bundle.SetVideoList(a);

    }

    [RelayCommand]
    private async Task LoadAudio()
    {
        var a = await _filePickerService.GetFilesFromFolder();

        if (a == null) return;

        foreach (var item in a)
        {
            Debug.WriteLine(item);
        }

        _mediaContext.Bundle.SetAudioList(a);
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
