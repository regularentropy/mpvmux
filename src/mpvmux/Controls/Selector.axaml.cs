using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Collections;
using System.Windows.Input;

namespace mpvmux.Controls;

public partial class Selector : UserControl
{
    public Selector()
    {
        InitializeComponent();
    }

    private string _rowName = "";

    public static readonly DirectProperty<Selector, string> RowNameProperty = AvaloniaProperty.RegisterDirect<Selector, string>(
        nameof(RowName), o => o.RowName, (o, v) => o.RowName = v);

    public string RowName
    {
        get => _rowName;
        set => SetAndRaise(RowNameProperty, ref _rowName, value);
    }

    private ICommand? _addCommand;

    public static readonly DirectProperty<Selector, ICommand?> AddCommandProperty =
        AvaloniaProperty.RegisterDirect<Selector, ICommand?>(
            nameof(AddCommand), o => o.AddCommand, (o, v) => o.AddCommand = v);

    public ICommand? AddCommand
    {
        get => _addCommand;
        set => SetAndRaise(AddCommandProperty, ref _addCommand, value);
    }

    private ICommand? _clearCommand;

    public static readonly DirectProperty<Selector, ICommand?> ClearCommandProperty =
        AvaloniaProperty.RegisterDirect<Selector, ICommand?>(
            nameof(ClearCommand), o => o.ClearCommand, (o, v) => o.ClearCommand = v);

    public ICommand? ClearCommand
    {
        get => _clearCommand;
        set => SetAndRaise(ClearCommandProperty, ref _clearCommand, value);
    }

    private ICommand? _removeCommand;

    public static readonly DirectProperty<Selector, ICommand?> RemoveCommandProperty =
        AvaloniaProperty.RegisterDirect<Selector, ICommand?>(
            nameof(RemoveCommand), o => o.RemoveCommand, (o, v) => o.RemoveCommand = v);

    public ICommand? RemoveCommand
    {
        get => _removeCommand;
        set => SetAndRaise(RemoveCommandProperty, ref _removeCommand, value);
    }

    private int _rowSelectedIndex;

    public static readonly DirectProperty<Selector, int> RowSelectedIndexProperty = AvaloniaProperty.RegisterDirect<Selector, int>(
        nameof(RowSelectedIndex), o => o.RowSelectedIndex, (o, v) => o.RowSelectedIndex = v);

    public int RowSelectedIndex
    {
        get => _rowSelectedIndex;
        set => SetAndRaise(RowSelectedIndexProperty, ref _rowSelectedIndex, value);
    }

    private IEnumerable? _itemsSource;

    public static readonly DirectProperty<Selector, IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.RegisterDirect<Selector, IEnumerable?>(
            nameof(ItemsSource),
            o => o.ItemsSource,
            (o, v) => o.ItemsSource = v);

    public IEnumerable? ItemsSource
    {
        get => _itemsSource;
        set => SetAndRaise(ItemsSourceProperty, ref _itemsSource, value);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || RemoveCommand is null)
            return;

        if (sender is not ListBox { SelectedItem: { } selected })
            return;

        if (!RemoveCommand.CanExecute(selected))
            return;

        RemoveCommand.Execute(selected);
        e.Handled = true;
    }
}
