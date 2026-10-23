using Avalonia.Controls;
using mpvmux.Services;

namespace mpvmux.Views;

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow(string title, string description)
    {
        InitializeComponent();
        Title = title;
        Description.Text = description;
    }

    public UnsavedChangesWindow()
    {
    }

    private void SaveButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(UnsavedChangesChoice.Save);

    private void DiscardButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(UnsavedChangesChoice.Discard);

    private void CancelButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(UnsavedChangesChoice.Cancel);
}
