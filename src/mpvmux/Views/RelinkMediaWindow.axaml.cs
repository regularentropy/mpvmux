using Avalonia.Controls;
using Avalonia.Interactivity;
using mpvmux.ViewModels;

namespace mpvmux.Views;

public partial class RelinkMediaWindow : Window
{
    public RelinkMediaWindow()
    {
        InitializeComponent();
    }

    private void ApplyButton_OnClick(object? sender, RoutedEventArgs e) =>
        Close((DataContext as RelinkMediaViewModel)?.BuildSelections());

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);
}
