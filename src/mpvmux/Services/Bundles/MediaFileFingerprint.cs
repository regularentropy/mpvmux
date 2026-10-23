using mpvmux.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace mpvmux.Services;

/// <summary>
/// Records size, last write time and a sampled content hash for every referenced media file so a
/// later rename can be recognised. The fields are optional: bundles written before they existed,
/// or records whose file is currently unavailable, simply carry no fingerprint.
/// </summary>
internal static class MediaFileFingerprint
{
    /// <summary>
    /// How much of each end of a file goes into <see cref="MediaRecord.ContentHash"/>. Sampling
    /// keeps the cost bounded for multi-gigabyte media while still surviving a rename.
    /// </summary>
    public const int ContentHashSampleBytes = 64 * 1024;

    /// <summary>
    /// Refreshes the fingerprints of a bundle in place. Best effort by design: a save must never
    /// fail because a media file is missing, locked or on an unreadable drive.
    /// </summary>
    public static void Capture(MediaBundleModel bundle)
    {
        CaptureList(bundle.VideoList);
        CaptureList(bundle.AudioList);
        CaptureList(bundle.SubtitleList);
    }

    /// <summary>
    /// SHA-256 over the file length, its first 64 KiB and its last 64 KiB, as lowercase hex.
    /// Content is what survives a rename, so this identifies a file whose name, folder and
    /// timestamps all changed. Returns null when the file cannot be read or is locked.
    /// </summary>
    public static string? ComputeContentHash(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, FileOptions.SequentialScan);

            var length = stream.Length;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            // length first, so two different files can only collide on the sampled bytes
            hash.AppendData(BitConverter.GetBytes(length));

            var buffer = new byte[ContentHashSampleBytes];
            var first = ReadAtMost(stream, buffer);
            hash.AppendData(buffer, 0, first);

            if (length > ContentHashSampleBytes)
            {
                stream.Seek(Math.Max(0, length - ContentHashSampleBytes), SeekOrigin.Begin);
                var last = ReadAtMost(stream, buffer);
                hash.AppendData(buffer, 0, last);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static void CaptureList(ObservableCollection<MediaRecord> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var record = list[i];
            if (record is null || string.IsNullOrWhiteSpace(record.Path))
                continue;

            // Keep an older fingerprint when the file cannot be read right now: it may be the
            // only clue that identifies the file after a rename, and clearing it would lose it.
            var info = TryRead(record.Path);
            if (info is null)
                continue;

            // Hashing every media file on every save would be wasteful; the metadata that
            // changes on a write also changes when the content does, so only re-read then.
            var unchanged = record.SizeBytes == info.Length
                && record.ModifiedUtc == info.LastWriteTimeUtc
                && record.ContentHash is not null;

            var contentHash = unchanged ? record.ContentHash : ComputeContentHash(record.Path);
            if (contentHash is null)
                continue;

            var updated = record with
            {
                SizeBytes = info.Length,
                ModifiedUtc = info.LastWriteTimeUtc,
                ContentHash = contentHash
            };

            // replacing a record raises collection notifications, so only touch what changed
            if (!updated.Equals(record))
                list[i] = updated;
        }
    }

    private static int ReadAtMost(Stream stream, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static FileInfo? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // a hand-edited bundle can contain a path that is not legal on this platform
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
