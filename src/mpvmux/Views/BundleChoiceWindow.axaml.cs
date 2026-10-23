using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using mpvmux.ViewModels;

namespace mpvmux.Views;

public partial class BundleChoiceWindow : Window
{
    public BundleChoiceWindow()
    {
        InitializeComponent();
    }

    private void OpenButton_OnClick(object? sender, RoutedEventArgs e) => Close(SelectedPath);

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void BundleList_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedPath is not null)
            Close(SelectedPath);
    }

    private string? SelectedPath => (DataContext as BundleChoiceViewModel)?.SelectedPath;
}
