using Avalonia.Controls;
using Avalonia.Input;
using mpvmux.ViewModels;
using System;
using System.Linq;

namespace mpvmux.Controls;

public partial class TrackTable : UserControl
{
    private readonly Border[] _dropOverlays;

    public TrackTable()
    {
        InitializeComponent();

        _dropOverlays = [VideoDropOverlay, AudioDropOverlay, SubtitleDropOverlay];

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var section = GetSection(e);
        if (section is null || !HasFiles(e))
        {
            HideDropOverlays();
            e.DragEffects = DragDropEffects.None;
            return;
        }

        ShowDropOverlay(section.Value);
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => HideDropOverlays();

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        HideDropOverlays();

        var section = GetSection(e);
        if (section is null || DataContext is not TrackTableViewModel viewModel)
            return;

        var paths = e.DataTransfer.TryGetFiles()?
            .Select(file => file.Path)
            .Where(uri => uri.IsFile)
            .Select(uri => uri.LocalPath)
            .ToArray();

        if (paths is not { Length: > 0 })
            return;

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;

        await viewModel.LoadDroppedAsync(section.Value, paths);
    }

    private static bool HasFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { Length: > 0 };

    private bool HasRows => DataContext is TrackTableViewModel { HasRows: true };

    private TrackSection? GetSection(DragEventArgs e)
    {
        // before the first row exists there are no columns to aim at, so the whole
        // empty table is a single field that loads videos
        if (!HasRows)
            return TrackSection.Video;

        // the three columns are equal thirds of the table, so the pointer's x picks the target
        var width = RootGrid.Bounds.Width;
        if (width <= 0)
            return null;

        var column = Math.Clamp((int)(e.GetPosition(RootGrid).X / (width / 3)), 0, 2);
        return column switch
        {
            0 => TrackSection.Video,
            1 => TrackSection.Audio,
            _ => TrackSection.Subtitle
        };
    }

    private void ShowDropOverlay(TrackSection section)
    {
        // the empty table is one drop field, so its highlight spans all three columns
        var singleField = !HasRows;
        Grid.SetColumnSpan(VideoDropOverlay, singleField ? 3 : 1);

        var visible = singleField ? 0 : (int)section;
        for (var i = 0; i < _dropOverlays.Length; i++)
            _dropOverlays[i].IsVisible = i == visible;
    }

    private void HideDropOverlays()
    {
        foreach (var overlay in _dropOverlays)
            overlay.IsVisible = false;
    }
}
