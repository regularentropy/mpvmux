using System.Collections.Generic;

namespace mpvmux.Models;

/// <summary>
/// A user-visible action whose keyboard shortcut can be changed in Preferences.
/// <see cref="Id"/> is also the key used in the persisted override map, so it must stay stable.
/// </summary>
internal sealed record ShortcutAction(string Id, string Label, string DefaultGesture);

internal static class ShortcutActions
{
    public const string NewBundle = "File.New";
    public const string OpenBundle = "File.Open";
    public const string SaveBundle = "File.Save";
    public const string SaveBundleAs = "File.SaveAs";
    public const string Play = "Playback.Play";
    public const string Pause = "Playback.Pause";
    public const string Stop = "Playback.Stop";
    public const string PreviousTrack = "Playback.Previous";
    public const string NextTrack = "Playback.Next";
    public const string ToggleAutoplay = "Playback.Autoplay";
    public const string ToggleFullscreen = "Playback.Fullscreen";
    public const string CheckForUpdates = "Help.CheckForUpdates";
    public const string About = "Help.About";

    // Alt+F4 is handled by the window manager, so Exit is intentionally not configurable.
    public static IReadOnlyList<ShortcutAction> All { get; } =
    [
        new(NewBundle, "New bundle", "Ctrl+N"),
        new(OpenBundle, "Open bundle", "Ctrl+O"),
        new(SaveBundle, "Save bundle", "Ctrl+S"),
        new(SaveBundleAs, "Save bundle as", "Ctrl+Shift+S"),
        new(Play, "Play", "Ctrl+Enter"),
        new(Pause, "Pause", "Ctrl+P"),
        new(Stop, "Stop", "Ctrl+Shift+Enter"),
        new(PreviousTrack, "Previous video", "Ctrl+Left"),
        new(NextTrack, "Next video", "Ctrl+Right"),
        new(ToggleAutoplay, "Autoplay", "Ctrl+Shift+A"),
        new(ToggleFullscreen, "Start playback in fullscreen", "Ctrl+Shift+F"),
        new(CheckForUpdates, "Check for updates", "F5"),
        new(About, "About mpvmux", "F1"),
    ];
}
