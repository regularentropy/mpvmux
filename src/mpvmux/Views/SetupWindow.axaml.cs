using Avalonia.Controls;
using Avalonia.Interactivity;
using mpvmux.ViewModels;

namespace mpvmux.Views;

public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is SetupWindowViewModel vm)
                await vm.InitializeAsync();
        };

        // closing without saving must not bring the setup back on the next start
        Closed += (_, _) => (DataContext as SetupWindowViewModel)?.Dismiss();
    }

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWindowViewModel vm && await vm.TrySaveAsync())
            Close();
    }

    private void NotNowButton_OnClick(object? sender, RoutedEventArgs e) => Close();
}
