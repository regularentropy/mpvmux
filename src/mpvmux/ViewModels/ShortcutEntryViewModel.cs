using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Models;

namespace mpvmux.ViewModels;

/// <summary>
/// One editable shortcut row in Preferences. Changes stay pending here until Save.
/// </summary>
internal partial class ShortcutEntryViewModel : ObservableObject
{
    public ShortcutEntryViewModel(ShortcutAction action, KeyGesture gesture)
    {
        Action = action;
        DefaultGesture = KeyGesture.Parse(action.DefaultGesture);
        _gesture = gesture;
    }

    public ShortcutAction Action { get; }
    public string Id => Action.Id;
    public string Label => Action.Label;
    public KeyGesture DefaultGesture { get; }

    [ObservableProperty]
    private KeyGesture _gesture;

    /// <summary>Whether the action is also captured while mpvmux is not focused.</summary>
    [ObservableProperty]
    private bool _isGlobal;

    public string GestureText => Gesture.ToString();

    partial void OnGestureChanged(KeyGesture value) => OnPropertyChanged(nameof(GestureText));

    public void Reset() => Gesture = DefaultGesture;
}
