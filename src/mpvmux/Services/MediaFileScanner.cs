using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mpvmux.Services;

/// <summary>
/// Turns dropped paths or a folder into the list of supported media files for a column.
/// </summary>
internal static class MediaFileScanner
{
    public static List<MediaRecord> Collect(IEnumerable<string> paths, string[] extensions)
    {
        var allowed = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        return paths
            .SelectMany(path => CollectFromPath(path, allowed))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new MediaRecord(Path.GetFileNameWithoutExtension(path), path))
            .ToList();
    }

    public static List<MediaRecord> FindMediaFiles(string folder, HashSet<string> allowedExtensions) =>
        Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new MediaRecord(Path.GetFileNameWithoutExtension(f), f))
            .ToList();

    private static IEnumerable<string> CollectFromPath(string path, HashSet<string> allowedExtensions)
    {
        if (Directory.Exists(path))
            return Directory
                .EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)));

        // a dropped file only counts when it matches the column it was dropped on
        return File.Exists(path) && allowedExtensions.Contains(Path.GetExtension(path))
            ? [path]
            : [];
    }
}
