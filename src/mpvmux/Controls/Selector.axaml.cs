using Avalonia;
using Avalonia.Controls;
using System.Collections;

namespace mpvmux.Controls;

public partial class Selector : UserControl
{
    public Selector()
    {
        InitializeComponent();
    }

    private string _rowName;

    public static readonly DirectProperty<Selector, string> RowNameProperty = AvaloniaProperty.RegisterDirect<Selector, string>(
        nameof(RowName), o => o.RowName, (o, v) => o.RowName = v);

    public string RowName
    {
        get => _rowName;
        set => SetAndRaise(RowNameProperty, ref _rowName, value);
    }

    private int _rowSelectedIndex;

    public static readonly DirectProperty<Selector, int> RowSelectedIndexProperty = AvaloniaProperty.RegisterDirect<Selector, int>(
        nameof(RowSelectedIndex), o => o.RowSelectedIndex, (o, v) => o.RowSelectedIndex = v);

    public int RowSelectedIndex
    {
        get => _rowSelectedIndex;
        set => SetAndRaise(RowSelectedIndexProperty, ref _rowSelectedIndex, value);
    }

    private IEnumerable _itemsSource;

    public static readonly DirectProperty<Selector, IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.RegisterDirect<Selector, IEnumerable>(
            nameof(ItemsSource),
            o => o.ItemsSource,
            (o, v) => o.ItemsSource = v);

    public IEnumerable ItemsSource
    {
        get => _itemsSource;
        set => SetAndRaise(ItemsSourceProperty, ref _itemsSource, value);
    }
}