using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using mpvmux.ViewModels;

namespace mpvmux.Views;

public partial class PreferencesWindow : Window
{
    private ShortcutEntryViewModel? _capturing;

    public PreferencesWindow()
    {
        InitializeComponent();

        // tunnel so the captured key never reaches the focused button
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Closed += (_, _) =>
        {
            EndCapture();
            (DataContext as PreferencesWindowViewModel)?.Detach();
        };
    }

    private void ChangeShortcut_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ShortcutEntryViewModel entry })
            return;

        (DataContext as PreferencesWindowViewModel)?.BeginShortcutCapture();
        _capturing = entry;
        ShortcutError.IsVisible = false;
        ShortcutHint.Text = $"Press the new shortcut for \u201c{entry.Label}\u201d.";
        ShortcutHint.IsVisible = true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is not { } entry)
            return;

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndCapture();
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            entry.Reset();
            EndCapture();
            return;
        }

        // the modifier keys themselves are not a shortcut
        if (IsModifierKey(e.Key))
            return;

        if (DataContext is not PreferencesWindowViewModel vm)
            return;

        var error = vm.TrySetShortcut(entry, new KeyGesture(e.Key, e.KeyModifiers));
        if (error is not null)
        {
            ShortcutError.Text = error;
            ShortcutError.IsVisible = true;
            return;
        }

        EndCapture();
    }

    private void EndCapture()
    {
        if (_capturing is not null)
            (DataContext as PreferencesWindowViewModel)?.EndShortcutCapture();

        _capturing = null;
        ShortcutHint.IsVisible = false;
        ShortcutError.IsVisible = false;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EndCapture();

        if (DataContext is PreferencesWindowViewModel vm && vm.TryApply())
            Close();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EndCapture();
        Close();
    }
}
