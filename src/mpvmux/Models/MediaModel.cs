namespace mpvmux.Models;

internal record MediaRecord(string Name, string Path)
{
    /// <summary>
    /// An empty slot. Optional track lists use it to keep a cell aligned with its video row
    /// after the user clears that single cell.
    /// </summary>
    public static MediaRecord Placeholder { get; } = new("", "");
}
