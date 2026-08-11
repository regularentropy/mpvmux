using Avalonia.Controls;

namespace mpvmux;

public partial class UpdaterWindow : Window
{
    public UpdaterWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}