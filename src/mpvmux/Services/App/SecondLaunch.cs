using System;
using System.Collections.Generic;

namespace mpvmux.Services;

/// <summary>
/// What a launch does when another mpvmux already owns the data folder. Kept free of Avalonia
/// and processes so the decision can be tested.
/// </summary>
internal static class SecondLaunch
{
    /// <summary>
    /// True when the launch should start Avalonia only to say that mpvmux is already running.
    /// False means it is done: the bundles it was given reached the running instance.
    /// </summary>
    public static bool ShouldShowAlreadyRunning(IReadOnlyList<string> bundlePaths, Func<string, bool> forward)
    {
        if (bundlePaths.Count == 0)
            return true;

        // One delivered path is enough: the user asked for that bundle, and it is now opening.
        var forwardedAny = false;

        foreach (var path in bundlePaths)
            forwardedAny |= forward(path);

        return !forwardedAny;
    }
}
