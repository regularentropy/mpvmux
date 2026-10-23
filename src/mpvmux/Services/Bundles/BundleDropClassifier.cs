using mpvmux.Constants;
using System;
using System.Collections.Generic;
using System.IO;

namespace mpvmux.Services;

/// <summary>
/// A dropped bundle is mpvmux's document type, so it opens that bundle instead of being
/// scanned as media — no matter which table column it was dropped on.
/// </summary>
internal static class BundleDropClassifier
{
    /// <summary>
    /// The first dropped <c>.mpvm</c> path, or <c>null</c> when the drop carries no bundle.
    /// A bundle wins even when the drop also contains media files.
    /// </summary>
    public static string? FindBundle(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.Equals(Path.GetExtension(path), AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }
}
