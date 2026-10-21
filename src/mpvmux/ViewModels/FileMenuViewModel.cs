﻿using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class FileMenuViewModel(
    IFilePickerService filePickerService,
    IBundleFileService bundleFileService,
    IBundleRepository bundleRepository,
    HistoryViewModel historyViewModel,
    IDialogHelper dialogHelper) : ViewModelBase
{
    [RelayCommand]
    private async Task NewFile()
    {
        var path = await filePickerService.SaveFilePickerAsync();
        if (path == null) return;

        var saved = await TrySaveAsync(async () =>
        {
            bundleFileService.NewFile();
            return await bundleFileService.SaveAsAsync(path);
        });

        if (saved)
            historyViewModel.SelectByPath(path);
    }

    [RelayCommand]
    private async Task LoadFile()
    {
        var path = await filePickerService.OpenFilePickerAsync();
        if (path == null) return;

        // goes through the history's load lock so an open cannot race a history selection
        await historyViewModel.OpenAsync(path);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        var path = await filePickerService.SaveFilePickerAsync();
        if (path == null) return;

        var saved = await TrySaveAsync(() => bundleFileService.SaveAsAsync(path));
        if (saved)
            historyViewModel.SelectByPath(path);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrEmpty(bundleRepository.SelectedFilePath))
        {
            await SaveAs();
            return;
        }

        await TrySaveAsync(() => bundleFileService.SaveAsync());
    }

    /// <summary>
    /// Reports a failed write instead of letting the command task fault. The in-memory bundle
    /// is untouched, so the user can retry or Save As somewhere else. A successful save that
    /// could not update the backup still succeeds, with a non-blocking warning.
    /// </summary>
    private async Task<bool> TrySaveAsync(Func<Task<BundleSaveResult>> save)
    {
        try
        {
            var result = await save();

            if (result.HasWarning)
                dialogHelper.ShowInfo(result.Warning!);

            return true;
        }
        catch (Exception ex) when (ex is BundleSaveException or IOException or UnauthorizedAccessException)
        {
            dialogHelper.ShowError($"Could not save the bundle: {ex.Message}");
            return false;
        }
    }
}
