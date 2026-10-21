using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace mpvmux.Models;

internal partial class ConfigModel : ObservableObject
{
    public const int DefaultRecentLimit = 10;
    public const int MinRecentLimit = 1;
    public const int MaxRecentLimit = 50;

    [ObservableProperty]
    [property: JsonPropertyName("MPVPath")]
    private string _mpvPath = "mpv";

    /// <summary>
    /// True once the mpv setup was completed or explicitly dismissed. Until then the app looks
    /// for mpv on startup and offers the setup window when it cannot find a usable copy.
    /// </summary>
    [ObservableProperty] private bool _mpvSetupCompleted = false;

    [ObservableProperty] private bool _enableAutomaticPlayback = false;

    [ObservableProperty] private bool _enableFullscreen = false;

    [ObservableProperty] private bool _checkForUpdatesOnStartup = false;

    [ObservableProperty] private bool _enableGlobalShortcuts = false;

    [ObservableProperty] private bool _useDarkTheme = false;

    /// <summary>
    /// How many unpinned bundles the Recent dropdown shows. Pinned bundles are always listed
    /// on top of the limit, which is clamped to
    /// <see cref="MinRecentLimit"/>..<see cref="MaxRecentLimit"/> when used.
    /// </summary>
    [ObservableProperty] private int _recentLimit = DefaultRecentLimit;

    /// <summary>
    /// Configured shortcut gestures by <see cref="ShortcutAction.Id"/>. Only actions that differ
    /// from their default are stored, so new defaults reach users who never customised them.
    /// </summary>
    [ObservableProperty] private Dictionary<string, string> _shortcutOverrides = new();
}
