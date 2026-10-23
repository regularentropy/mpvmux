using mpvmux.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace mpvmux.Services;

/// <summary>
/// What mpvmux cares about on its command line: existing bundle files. Everything else is
/// ignored, so a stray argument or a switch never turns into an error dialog.
/// </summary>
internal static class LaunchArguments
{
    /// <summary>
    /// Existing <c>.mpvm</c> files named on the command line, as absolute paths in argument
    /// order. A second launch forwards these to the running instance.
    /// </summary>
    public static IReadOnlyList<string> BundlePaths(IEnumerable<string>? args)
    {
        if (args is null)
            return [];

        var paths = new List<string>();

        foreach (var argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument))
                continue;

            if (!argument.EndsWith(AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(argument);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            // only files that are actually there: forwarding a dead path would raise an error
            // dialog in the running instance for something the user typed by mistake
            if (!File.Exists(fullPath))
                continue;

            if (paths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                continue;

            paths.Add(fullPath);
        }

        return paths;
    }
}
