using System;
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
    /// File length when the bundle was last saved, used to recognise a renamed file. Optional:
    /// bundles written before this field existed load with <c>null</c>, and unknown members are
    /// skipped by older builds, so the schema version does not change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SizeBytes { get; init; }

    /// <summary>
    /// Last write time (UTC) when the bundle was last saved, paired with
    /// <see cref="SizeBytes"/> for rename detection. Never shown in the UI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ModifiedUtc { get; init; }

    /// <summary>
    /// SHA-256 over the file length and its first/last 64 KiB, recorded at save time. Content
    /// survives a rename, so this identifies a renamed file even when its name, folder and
    /// timestamps changed. Sampled rather than whole-file because bundles can reference
    /// multi-gigabyte media. Optional and additive like the other fingerprint fields.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentHash { get; init; }

    /// <summary>
    /// An empty slot. Optional track lists use it to keep a cell aligned with its video row
    /// after the user clears that single cell.
    /// </summary>
    public static MediaRecord Placeholder { get; } = new("", "");
}
