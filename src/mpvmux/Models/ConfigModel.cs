using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace mpvmux.Models;

internal partial class ConfigModel : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("MPVPath")]
    private string _mpvPath = "mpv";

    [ObservableProperty] private bool _enableAutomaticPlayback = false;

    [ObservableProperty] private bool _enableFullscreen = false;

    [ObservableProperty] private bool _checkForUpdatesOnStartup = false;

    [ObservableProperty] private bool _useDarkTheme = false;
}
