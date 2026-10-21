using Avalonia.Controls;
using Avalonia.Input;
using mpvmux.ViewModels;

namespace mpvmux.Controls;

public partial class TrackTable : UserControl
{
    public TrackTable()
    {
        InitializeComponent();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        if (sender is not ListBox { SelectedItem: TrackRow row })
            return;

        if (DataContext is not TrackTableViewModel viewModel)
            return;

        if (!viewModel.RemoveVideoCommand.CanExecute(row))
            return;

        viewModel.RemoveVideoCommand.Execute(row);
        e.Handled = true;
    }
}
