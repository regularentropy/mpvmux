using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class FileMenuViewModel(
    IFilePickerService filePickerService,
    IBundleFileService bundleFileService,
    IBundleRepository bundleRepository,
    HistoryViewModel historyViewModel) : ViewModelBase
{
    [RelayCommand]
    private async Task NewFile()
    {
        var path = await filePickerService.SaveFilePickerAsync();
        if (path == null) return;

        bundleFileService.NewFile();
        await bundleFileService.SaveAsAsync(path);
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

        await bundleFileService.SaveAsAsync(path);
        historyViewModel.SelectByPath(path);
    }

    [RelayCommand]
    private Task Save()
    {
        if (string.IsNullOrEmpty(bundleRepository.SelectedFilePath))
            return SaveAs();

        return bundleFileService.SaveAsync();
    }
}
