using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal enum BundleLoadError
{
    None,
    Missing,
    Unreadable,
    TooLarge,
    InvalidJson,
    InvalidShape,
    NewerSchema
}

/// <summary>
/// Outcome of reading a bundle. A failed result never carries a partially loaded model, so
/// callers can keep the current session untouched.
/// </summary>
internal sealed record BundleLoadResult(
    MediaBundleModel? Bundle,
    BundleLoadError Error,
    string Message,
    bool CanRestoreBackup = false)
{
    public bool IsSuccess => Bundle is not null && Error == BundleLoadError.None;

    public static BundleLoadResult Success(MediaBundleModel bundle) =>
        new(bundle, BundleLoadError.None, "");

    public static BundleLoadResult Failure(BundleLoadError error, string message) =>
        new(null, error, message);
}

internal sealed class BundleSaveException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Reads, validates, normalizes and writes <c>.mpvm</c> bundles. Validation happens on a
/// detached model so a malformed file can never reach <see cref="BundleSession"/>.
/// </summary>
internal static class BundleSerializer
{
    /// <summary>
    /// Bumped when the on-disk shape changes incompatibly. Files without the field are
    /// version 1, which is what every bundle written before versioning existed contains.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Bundles only reference paths, so anything larger is treated as corrupt.</summary>
    public const long MaxBundleBytes = 4L * 1024 * 1024;

    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<BundleLoadResult> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return BundleLoadResult.Failure(
                    BundleLoadError.Missing,
                    $"The bundle file '{filePath}' no longer exists.");

            if (info.Length > MaxBundleBytes)
                return BundleLoadResult.Failure(
                    BundleLoadError.TooLarge,
                    $"The bundle file is {info.Length / 1024 / 1024} MB, which is larger than the {MaxBundleBytes / 1024 / 1024} MB limit.");

            string text;
            using (var stream = new FileStream(
                       filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            return Parse(text, filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BundleLoadResult.Failure(
                BundleLoadError.Unreadable,
                $"The bundle file cannot be read: {ex.Message}");
        }
        catch (IOException ex)
        {
            return BundleLoadResult.Failure(
                BundleLoadError.Unreadable,
                $"The bundle file cannot be read: {ex.Message}");
        }
    }

    public static BundleLoadResult Parse(string text, string filePath)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BundleLoadResult.Failure(
                BundleLoadError.InvalidJson,
                "The bundle file is empty or truncated.");

        MediaBundleModel? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<MediaBundleModel>(text, Options);
        }
        catch (JsonException ex)
        {
            return BundleLoadResult.Failure(
                BundleLoadError.InvalidJson,
                $"The bundle file is not valid JSON: {ex.Message}");
        }

        if (bundle is null)
            return BundleLoadResult.Failure(
                BundleLoadError.InvalidShape,
                "The bundle file does not contain a bundle object.");

        if (bundle.SchemaVersion > CurrentSchemaVersion)
            return BundleLoadResult.Failure(
                BundleLoadError.NewerSchema,
                $"The bundle was written by a newer version of mpvmux (format {bundle.SchemaVersion}; this build supports {CurrentSchemaVersion}).");

        // missing/zero in older files means the original, unversioned shape
        if (bundle.SchemaVersion <= 0)
            bundle.SchemaVersion = CurrentSchemaVersion;

        Normalize(bundle);

        return BundleLoadResult.Success(bundle);
    }

    public static string Serialize(MediaBundleModel bundle)
    {
        bundle.SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(bundle, Options);
    }

    public static async Task<bool> IsValidFileAsync(string filePath, CancellationToken cancellationToken = default) =>
        (await LoadAsync(filePath, cancellationToken).ConfigureAwait(false)).IsSuccess;

    /// <summary>
    /// Repairs everything the UI relies on: non-null aligned collections, trimmed text,
    /// records derived from paths, no duplicate rows, and selection/volume inside their ranges.
    /// </summary>
    internal static void Normalize(MediaBundleModel bundle)
    {
        bundle.VideoList = NormalizeList(bundle.VideoList);
        bundle.AudioList = NormalizeList(bundle.AudioList);
        bundle.SubtitleList = NormalizeList(bundle.SubtitleList);

        RemoveDuplicateRows(bundle);

        if (bundle.VideoList.Count == 0)
            bundle.SelectedVideoIndex = -1;
        else if (bundle.SelectedVideoIndex >= bundle.VideoList.Count)
            bundle.SelectedVideoIndex = bundle.VideoList.Count - 1;
        else if (bundle.SelectedVideoIndex < -1)
            bundle.SelectedVideoIndex = -1;

        bundle.Volume = Math.Clamp(bundle.Volume, 0, 100);
    }

    private static ObservableCollection<MediaRecord> NormalizeList(ObservableCollection<MediaRecord>? list)
    {
        var normalized = new ObservableCollection<MediaRecord>();

        if (list is null)
            return normalized;

        foreach (var record in list)
        {
            // JSON arrays can contain nulls even though the model says otherwise. Keep a
            // placeholder so audio/subtitle cells stay aligned with their video row.
            if (record is null)
            {
                normalized.Add(MediaRecord.Placeholder);
                continue;
            }

            var name = (record.Name ?? "").Trim();
            var path = (record.Path ?? "").Trim();

            if (name.Length == 0 && path.Length > 0)
                name = Path.GetFileNameWithoutExtension(path);

            normalized.Add(record with { Name = name, Path = path });
        }

        return normalized;
    }

    /// <summary>
    /// Removes rows whose full video/audio/subtitle triple repeats, and rows that are entirely
    /// empty. All three lists are filtered with the same mask so row alignment survives.
    /// </summary>
    private static void RemoveDuplicateRows(MediaBundleModel bundle)
    {
        var count = Math.Max(
            bundle.VideoList.Count,
            Math.Max(bundle.AudioList.Count, bundle.SubtitleList.Count));

        if (count == 0)
            return;

        var keep = new bool[count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keepCount = 0;

        for (var i = 0; i < count; i++)
        {
            var video = PathAt(bundle.VideoList, i);
            var audio = PathAt(bundle.AudioList, i);
            var subtitle = PathAt(bundle.SubtitleList, i);

            if (video.Length == 0 && audio.Length == 0 && subtitle.Length == 0)
                continue;

            var key = $"{video}\u001f{audio}\u001f{subtitle}";
            if (!seen.Add(key))
                continue;

            keep[i] = true;
            keepCount++;
        }

        if (keepCount == count)
            return;

        bundle.VideoList = Filter(bundle.VideoList, keep);
        bundle.AudioList = Filter(bundle.AudioList, keep);
        bundle.SubtitleList = Filter(bundle.SubtitleList, keep);
    }

    private static string PathAt(ObservableCollection<MediaRecord> list, int index) =>
        index < list.Count ? list[index]?.Path ?? "" : "";

    private static ObservableCollection<MediaRecord> Filter(
        ObservableCollection<MediaRecord> list,
        bool[] keep)
    {
        var filtered = new ObservableCollection<MediaRecord>(
            list.Where((_, index) => index < keep.Length && keep[index]));

        return filtered;
    }
}
