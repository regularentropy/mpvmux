using Avalonia.Controls;
using Avalonia.Interactivity;
using mpvmux.ViewModels;

namespace mpvmux.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesWindowViewModel vm && vm.TryApply())
            Close();
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
