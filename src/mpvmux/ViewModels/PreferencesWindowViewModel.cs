using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class PreferencesWindowViewModel(IConfigService cs, IFilePickerService fps, IDialogHelper dh) : ViewModelBase
{
    [ObservableProperty]
    private string _mpvPath = cs.Config.MpvPath;

    [RelayCommand]
    private async Task Browse()
    {
        var path = await fps.PickExecutableAsync();
        if (path is not null)
            MpvPath = path;
    }

    public bool TryApply()
    {
        if (string.IsNullOrWhiteSpace(MpvPath))
        {
            dh.ShowError("MPV path cannot be empty.");
            return false;
        }

        // a bare command ("mpv") is resolved from PATH; anything else must be a real file
        if (MpvPath.Contains(Path.DirectorySeparatorChar) && !File.Exists(MpvPath))
        {
            dh.ShowError($"MPV executable not found: {MpvPath}");
            return false;
        }

        cs.Config.MpvPath = MpvPath;
        cs.Save();
        return true;
    }
}
