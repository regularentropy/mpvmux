using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Models.DTO;

namespace mpvmux.Models;

internal partial class ConfigModel : ObservableObject
{
    [ObservableProperty] private string _mpvPath = "mpv";

    [ObservableProperty] private bool _enableAutomaticPlayback = false;

    public ConfigModelDTO ToDto()
    {
        return new ConfigModelDTO()
        {
            MPVPath = MpvPath,
            EnableAutomaticPlayback = EnableAutomaticPlayback
        };
    }

    public static ConfigModel FromDto(ConfigModelDTO dto)
    {
        return new ConfigModel()
        {
            MpvPath = dto.MPVPath,
            EnableAutomaticPlayback = dto.EnableAutomaticPlayback
        };
    }
}