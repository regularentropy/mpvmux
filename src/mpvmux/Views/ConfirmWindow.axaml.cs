using Avalonia.Controls;

namespace mpvmux.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string description)
    {
        InitializeComponent();
        Title = title;
        Description.Text = description;
    }

    public ConfirmWindow()
    {
    }

    private void OkButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);

    private void CancelButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
