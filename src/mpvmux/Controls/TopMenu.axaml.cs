using Avalonia.Controls;
using Avalonia.Interactivity;
using mpvmux.Models;
using mpvmux.ViewModels;

namespace mpvmux.Controls;

internal partial class TopMenu : UserControl
{
    public TopMenu()
    {
        InitializeComponent();
    }

    private void OnQuickRowClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MediaRecord record)
            return;

        if (DataContext is TopMenuViewModel viewModel)
            viewModel.History.OpenRecordCommand.Execute(record);

        RecentDropDown.Flyout?.Hide();
    }

    private void OnQuickPinClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MediaRecord record
            && DataContext is TopMenuViewModel viewModel)
        {
            viewModel.History.TogglePinCommand.Execute(record);
        }

        e.Handled = true;
    }

    private void OnQuickManageClick(object? sender, RoutedEventArgs e)
    {
        // Open the window before hiding: closing the flyout detaches its content and would
        // clear the button's command binding, so the action cannot be driven by Command here.
        if (DataContext is TopMenuViewModel viewModel)
            viewModel.History.ManageRecentBundlesCommand.Execute(null);

        RecentDropDown.Flyout?.Hide();
    }
}
