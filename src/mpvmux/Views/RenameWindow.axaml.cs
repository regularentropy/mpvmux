using Avalonia.Controls;
using Avalonia.Interactivity;

namespace mpvmux.Views;

public partial class RenameWindow : Window
{
    public RenameWindow(string currentName)
    {
        InitializeComponent();

        NameBox.Text = currentName;

        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public RenameWindow()
    {
        InitializeComponent();
    }

    private void NameBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        RenameButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

    private void RenameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
            return;

        Close(name);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
