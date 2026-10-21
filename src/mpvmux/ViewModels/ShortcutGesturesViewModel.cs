using Avalonia.Input;
using mpvmux.Models;
using mpvmux.Services;

namespace mpvmux.ViewModels;

/// <summary>
/// Current gestures as bindable values for the menu hints. Refreshes itself whenever the
/// shortcut configuration changes, so no view has to know about the persistence details.
/// </summary>
internal sealed class ShortcutGesturesViewModel : ViewModelBase
{
    private readonly IShortcutService _shortcutService;

    public ShortcutGesturesViewModel(IShortcutService shortcutService)
    {
        _shortcutService = shortcutService;
        shortcutService.Changed += (_, _) => Refresh();
    }

    public KeyGesture NewBundle => _shortcutService.GetGesture(ShortcutActions.NewBundle);
    public KeyGesture OpenBundle => _shortcutService.GetGesture(ShortcutActions.OpenBundle);
    public KeyGesture SaveBundle => _shortcutService.GetGesture(ShortcutActions.SaveBundle);
    public KeyGesture SaveBundleAs => _shortcutService.GetGesture(ShortcutActions.SaveBundleAs);
    public KeyGesture Play => _shortcutService.GetGesture(ShortcutActions.Play);
    public KeyGesture Pause => _shortcutService.GetGesture(ShortcutActions.Pause);
    public KeyGesture Stop => _shortcutService.GetGesture(ShortcutActions.Stop);
    public KeyGesture PreviousTrack => _shortcutService.GetGesture(ShortcutActions.PreviousTrack);
    public KeyGesture NextTrack => _shortcutService.GetGesture(ShortcutActions.NextTrack);
    public KeyGesture ToggleAutoplay => _shortcutService.GetGesture(ShortcutActions.ToggleAutoplay);
    public KeyGesture ToggleFullscreen => _shortcutService.GetGesture(ShortcutActions.ToggleFullscreen);
    public KeyGesture CheckForUpdates => _shortcutService.GetGesture(ShortcutActions.CheckForUpdates);
    public KeyGesture About => _shortcutService.GetGesture(ShortcutActions.About);

    private void Refresh()
    {
        OnPropertyChanged(nameof(NewBundle));
        OnPropertyChanged(nameof(OpenBundle));
        OnPropertyChanged(nameof(SaveBundle));
        OnPropertyChanged(nameof(SaveBundleAs));
        OnPropertyChanged(nameof(Play));
        OnPropertyChanged(nameof(Pause));
        OnPropertyChanged(nameof(Stop));
        OnPropertyChanged(nameof(PreviousTrack));
        OnPropertyChanged(nameof(NextTrack));
        OnPropertyChanged(nameof(ToggleAutoplay));
        OnPropertyChanged(nameof(ToggleFullscreen));
        OnPropertyChanged(nameof(CheckForUpdates));
        OnPropertyChanged(nameof(About));
    }
}
