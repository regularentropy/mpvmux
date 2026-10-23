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
    /// Every dropped <c>.mpvm</c> path in drop order, or an empty list when the drop carries
    /// no bundle. Bundles win even when the drop also contains media files.
    /// </summary>
    public static IReadOnlyList<string> FindBundles(IReadOnlyList<string> paths)
    {
        List<string>? bundles = null;

        foreach (var path in paths)
        {
            if (string.Equals(Path.GetExtension(path), AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
                (bundles ??= []).Add(path);
        }

        return bundles ?? [];
    }
}
