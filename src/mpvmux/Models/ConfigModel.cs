using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
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

    [ObservableProperty] private bool _enableGlobalShortcuts = false;

    [ObservableProperty] private bool _useDarkTheme = false;

    /// <summary>
    /// Configured shortcut gestures by <see cref="ShortcutAction.Id"/>. Only actions that differ
    /// from their default are stored, so new defaults reach users who never customised them.
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _shortcutOverrides = new();
}
