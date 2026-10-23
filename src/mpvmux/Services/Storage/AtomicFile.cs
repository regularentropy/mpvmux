using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// Crash-safe file replacement for the small JSON files mpvmux owns. Content is written to a
/// sibling temporary file, flushed to disk and then moved over the target, so an interrupted
/// write never leaves a truncated file behind.
/// </summary>
internal static class AtomicFile
{
    public static string TempPathFor(string path) => path + ".tmp";

    public static async Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        var tempPath = TempPathFor(path);

        // a previous crash may have left a half-written file behind; never reuse it
        DeleteIfExists(tempPath);

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // same directory, so the move is a rename and replaces the target atomically
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            DeleteIfExists(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Synchronous counterpart used by config.json and history.json, which are saved from UI
    /// paths (menu toggles, window closing) that do not await.
    /// </summary>
    public static void WriteAllText(string path, string content)
    {
        var tempPath = TempPathFor(path);

        DeleteIfExists(tempPath);

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.None))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            DeleteIfExists(tempPath);
            throw;
        }
    }

    public static void CleanupTemp(string path) => DeleteIfExists(TempPathFor(path));

    public static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // best effort: a leftover temp file is harmless, the next write replaces it
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
