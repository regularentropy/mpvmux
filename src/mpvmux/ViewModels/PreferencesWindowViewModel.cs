using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class PreferencesWindowViewModel(IConfigService configService, IFilePickerService filePickerService, IDialogHelper dialogHelper) : ViewModelBase
{
    [ObservableProperty]
    private string _mpvPath = configService.Config.MpvPath;

    [ObservableProperty]
    private bool _enableAutomaticPlayback = configService.Config.EnableAutomaticPlayback;

    [ObservableProperty]
    private bool _enableFullscreen = configService.Config.EnableFullscreen;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup = configService.Config.CheckForUpdatesOnStartup;

    [RelayCommand]
    private async Task Browse()
    {
        var path = await filePickerService.PickExecutableAsync();
        if (path is not null)
            MpvPath = path;
    }

    public bool TryApply()
    {
        if (string.IsNullOrWhiteSpace(MpvPath))
        {
            dialogHelper.ShowError("MPV path cannot be empty.");
            return false;
        }

        // a bare command ("mpv") is resolved from PATH; anything else must be a real file
        if (MpvPath.Contains(Path.DirectorySeparatorChar) && !File.Exists(MpvPath))
        {
            dialogHelper.ShowError($"MPV executable not found: {MpvPath}");
            return false;
        }

        configService.Config.MpvPath = MpvPath;
        configService.Config.EnableAutomaticPlayback = EnableAutomaticPlayback;
        configService.Config.EnableFullscreen = EnableFullscreen;
        configService.Config.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
        configService.Save();
        return true;
    }
}
