using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace mpvmux.Models;

internal partial class HistoryModel : ObservableObject
{
    [ObservableProperty] private string? _lastDatabasePath = "";

    [ObservableProperty] private ObservableCollection<MediaRecord> _history = [];

    /// <summary>
    /// Paths of bundles the user pinned. Persisted only in history.json, so .mpvm bundle files
    /// never carry history-only state.
    /// </summary>
    public List<string> PinnedPaths { get; set; } = [];

    /// <summary>
    /// Paths of bundles that exist but failed validation. Persisted here, not in the bundle,
    /// so a corrupt bundle never carries its own recovery state.
    /// </summary>
    public List<string> CorruptPaths { get; set; } = [];
}
