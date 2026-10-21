using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using mpvmux.Models;
using mpvmux.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.Views;

public partial class RecentBundlesWindow : Window
{
    private static readonly DataFormat<MediaRecord> EntryFormat =
        DataFormat.CreateInProcessFormat<MediaRecord>("mpvmux.recent-entry");

    private RecentBundlesViewModel? _viewModel;
    private MediaRecord? _dragRecord;

    public RecentBundlesWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        DragDrop.SetAllowDrop(PinnedList, true);
        PinnedList.AddHandler(DragDrop.DragOverEvent, OnPinnedDragOver);
        PinnedList.AddHandler(DragDrop.DragLeaveEvent, OnPinnedDragLeave);
        PinnedList.AddHandler(DragDrop.DropEvent, OnPinnedDrop);

        // tunnel so the grip wins over the list's own selection handling
        PinnedList.AddHandler(PointerPressedEvent, OnPinnedPointerPressed, RoutingStrategies.Tunnel);

        PinnedList.AddHandler(PointerPressedEvent, OnListPointerPressed);
        RecentList.AddHandler(PointerPressedEvent, OnListPointerPressed);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.CloseRequested -= OnCloseRequested;

        _viewModel = DataContext as RecentBundlesViewModel;

        if (_viewModel is not null)
            _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.Dispose();
            _viewModel = null;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
            return;

        if ((sender as ListBox)?.SelectedItem is not MediaRecord record)
            return;

        _viewModel.OpenCommand.Execute(record);
        e.Handled = true;
    }

    private void OnPinnedKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down)
        {
            if (PinnedList.SelectedItem is not MediaRecord record || _viewModel is null)
                return;

            var command = e.Key == Key.Up ? _viewModel.MoveUpCommand : _viewModel.MoveDownCommand;
            command.Execute(record);
            e.Handled = true;
            return;
        }

        OnListKeyDown(sender, e);
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is null || (sender as ListBox)?.SelectedItem is not MediaRecord record)
            return;

        _viewModel.OpenCommand.Execute(record);
        e.Handled = true;
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox list || !e.GetCurrentPoint(list).Properties.IsRightButtonPressed)
            return;

        if (FindAncestor<ListBoxItem>(e.Source as Visual) is { DataContext: MediaRecord record })
            list.SelectedItem = record;
    }

    private async void OnPinnedPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PinnedList).Properties.IsLeftButtonPressed || !IsDragHandle(e.Source))
            return;

        if (FindAncestor<ListBoxItem>(e.Source as Visual)?.DataContext is not MediaRecord record || !record.IsPinned)
            return;

        e.Handled = true;
        _dragRecord = record;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(EntryFormat, record));

        try
        {
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception)
        {
            // drag-and-drop may be unavailable on some platforms; keep the list usable
        }
        finally
        {
            _dragRecord = null;
            HideDropIndicator();
        }
    }

    private void OnPinnedDragOver(object? sender, DragEventArgs e)
    {
        if (_dragRecord is null)
        {
            e.DragEffects = DragDropEffects.None;
            HideDropIndicator();
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        ResolveDropTarget(e, showIndicator: true);
        e.Handled = true;
    }

    private void OnPinnedDragLeave(object? sender, DragEventArgs e) => HideDropIndicator();

    private void OnPinnedDrop(object? sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (_dragRecord is not { } record || _viewModel is null)
            return;

        var (target, placeAfter) = ResolveDropTarget(e, showIndicator: false);
        if (target is null)
            return;

        e.DragEffects = _viewModel.MovePinned(record, target, placeAfter)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Finds the pinned row under the pointer and whether the drop should land below it.
    /// Dropping in the empty space under the last row appends to the pinned group.
    /// </summary>
    private (MediaRecord? Target, bool PlaceAfter) ResolveDropTarget(DragEventArgs e, bool showIndicator)
    {
        var position = e.GetPosition(PinnedList);

        // Some platforms report the ListBox itself as the drag event source, so fall back to
        // hit-testing the position within the list.
        var item = FindAncestor<ListBoxItem>(e.Source as Visual)
            ?? PinnedList.GetVisualsAt(position).OfType<ListBoxItem>().FirstOrDefault();

        if (item?.DataContext is MediaRecord target && target.IsPinned)
        {
            var top = item.TranslatePoint(default, PinnedList)?.Y ?? 0;
            var placeAfter = position.Y > top + item.Bounds.Height / 2;

            if (showIndicator)
                ShowDropIndicator(top, item.Bounds.Height, placeAfter);

            return (target, placeAfter);
        }

        if (_viewModel is { PinnedEntries.Count: > 0 })
        {
            var last = _viewModel.PinnedEntries[^1];

            if (showIndicator && PinnedList.ContainerFromItem(last) is ListBoxItem lastItem)
            {
                var top = lastItem.TranslatePoint(default, PinnedList)?.Y ?? 0;
                ShowDropIndicator(top, lastItem.Bounds.Height, placeAfter: true);
            }

            return (last, true);
        }

        if (showIndicator)
            HideDropIndicator();

        return (null, false);
    }

    private void ShowDropIndicator(double itemTop, double itemHeight, bool placeAfter)
    {
        var y = placeAfter ? itemTop + itemHeight : itemTop;

        Canvas.SetTop(PinnedDropIndicator, Math.Max(0, y - 1));
        Canvas.SetLeft(PinnedDropIndicator, 4);
        PinnedDropIndicator.Width = Math.Max(0, PinnedList.Bounds.Width - 8);
        PinnedDropIndicator.IsVisible = true;
    }

    private void HideDropIndicator() => PinnedDropIndicator.IsVisible = false;

    private static bool IsDragHandle(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control control
                && (control.Name == "DragHandle" || control.Classes.Contains("dragHandle")))
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(Visual? visual) where T : Control
    {
        for (; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is T match)
                return match;
        }

        return null;
    }

    private MediaRecord? ContextRecord(object? sender)
    {
        if ((sender as Control)?.DataContext is MediaRecord direct)
            return direct;

        // Context menus live in a popup, so the record is taken from the row the menu is
        // attached to rather than from inherited DataContext when that is unavailable.
        if ((sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: { } target }
            && target.DataContext is MediaRecord fromTarget)
        {
            return fromTarget;
        }

        return PinnedList.SelectedItem as MediaRecord
            ?? RecentList.SelectedItem as MediaRecord;
    }

    private void OnContextOpen(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            _viewModel.OpenCommand.Execute(record);
    }

    private void OnContextTogglePin(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            _viewModel.TogglePinCommand.Execute(record);
    }

    private void OnContextMoveUp(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            _viewModel.MoveUpCommand.Execute(record);
    }

    private void OnContextMoveDown(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            _viewModel.MoveDownCommand.Execute(record);
    }

    private void OnContextMoveToTop(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            _viewModel.MoveToTopCommand.Execute(record);
    }

    private async void OnContextRename(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            await _viewModel.RenameCommand.ExecuteAsync(record);
    }

    private async void OnContextRemove(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && ContextRecord(sender) is { } record)
            await _viewModel.RemoveCommand.ExecuteAsync(record);
    }
}
