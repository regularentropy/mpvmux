using System.Text.Json.Serialization;

namespace mpvmux.Models;

internal record MediaRecord(string Name, string Path)
{
    /// <summary>
    /// Pins this record to the top of the recent list. History-only state: it is rebuilt from
    /// <see cref="HistoryModel.PinnedPaths"/> on load and never written into a bundle.
    /// </summary>
    [JsonIgnore]
    public bool IsPinned { get; init; }

    /// <summary>
    /// True when the bundle file is not on disk right now. Missing records stay in history so
    /// the user can re-link them; the flag is derived from the path and never persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsMissing { get; init; }

    /// <summary>
    /// True when the bundle exists but could not be read as a valid bundle. History-only state
    /// tracked in <see cref="HistoryModel.CorruptPaths"/> so the Manage window can offer recovery.
    /// </summary>
    [JsonIgnore]
    public bool IsCorrupt { get; init; }

    /// <summary>
    /// An empty slot. Optional track lists use it to keep a cell aligned with its video row
    /// after the user clears that single cell.
    /// </summary>
    public static MediaRecord Placeholder { get; } = new("", "");
}
