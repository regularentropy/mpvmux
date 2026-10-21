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
    /// An empty slot. Optional track lists use it to keep a cell aligned with its video row
    /// after the user clears that single cell.
    /// </summary>
    public static MediaRecord Placeholder { get; } = new("", "");
}
