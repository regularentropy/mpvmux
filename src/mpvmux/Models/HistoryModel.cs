using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace mpvmux.Models;

internal partial class HistoryModel : ObservableObject
{
    [ObservableProperty] private string? _lastDatabasePath = "";

    [ObservableProperty] private ObservableCollection<MediaRecord> _history = [];
}
