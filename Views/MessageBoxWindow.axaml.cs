using Avalonia.Controls;

namespace mpvmux;

public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow(string title, string description)
    {
        InitializeComponent();
        Title = title;
        Description.Text = description;
    }

    public MessageBoxWindow()
    {

    }

    private void CloseButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}