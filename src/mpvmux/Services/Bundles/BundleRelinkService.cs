using mpvmux.Constants;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleRelinkService
{
    /// <summary>
    /// Recursively looks for bundles with one of <paramref name="fileNames"/> under
    /// <paramref name="folder"/>. The result maps each file name to every match found, so the
    /// caller can decide what to do with ambiguous names.
    /// </summary>
    Task<Dictionary<string, List<string>>> FindByNameAsync(
        string folder,
        IReadOnlyCollection<string> fileNames,
        CancellationToken cancellationToken = default);
}

internal sealed class BundleRelinkService : IBundleRelinkService
{
    public Task<Dictionary<string, List<string>>> FindByNameAsync(
        string folder,
        IReadOnlyCollection<string> fileNames,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Scan(folder, fileNames, cancellationToken),
            cancellationToken);
    }

    private static Dictionary<string, List<string>> Scan(
        string folder,
        IReadOnlyCollection<string> fileNames,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var wanted = new HashSet<string>(
            fileNames.Where(name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0 || !Directory.Exists(folder))
            return found;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // symlinks and junctions can create cycles and duplicate hits
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(Path.GetExtension(file), AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileName(file);
            if (!wanted.Contains(name))
                continue;

            if (!found.TryGetValue(name, out var matches))
                found[name] = matches = [];

            matches.Add(file);
        }

        return found;
    }
}
