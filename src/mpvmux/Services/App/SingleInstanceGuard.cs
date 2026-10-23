using mpvmux.Constants;
using System;
using System.IO;
using System.Text;

namespace mpvmux.Services;

/// <summary>
/// Keeps mpvmux to one running instance per user. The lock is a file next to <c>config.json</c>
/// opened with <see cref="FileShare.None"/> and held for the process lifetime, so the operating
/// system drops it when the process exits — a crash cannot leave a stale block behind. The file
/// itself stays in place and holds the last PID for diagnostics.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    public const string LockFileName = "instance.lock";

    private FileStream? _stream;

    private SingleInstanceGuard(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>Where the default lock lives; tests point at their own temp directory.</summary>
    public static string LockFilePath => Path.Combine(AppConstants.BaseFolderPath, LockFileName);

    /// <summary>
    /// Takes the per-user instance lock. Completes with false when another mpvmux already holds
    /// it, which the caller reports instead of starting a second window.
    /// </summary>
    public static bool TryAcquire(out SingleInstanceGuard? guard, string? lockFilePath = null)
    {
        guard = null;
        var path = lockFilePath ?? LockFilePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an unusable data folder is reported by the storage services; never refuse to start
            // the app over the lock alone
            return true;
        }

        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);

            WriteProcessId(stream);
            guard = new SingleInstanceGuard(stream);
            return true;
        }
        catch (IOException)
        {
            // the file is open in another instance: that is the answer we were looking for
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // the lock file is not ours to open (hand-edited ACLs, read-only folder): the sharing
            // check cannot be enforced, but refusing to start would be worse than two windows
            return true;
        }
    }

    private static void WriteProcessId(FileStream stream)
    {
        try
        {
            stream.SetLength(0);
            var pid = Encoding.UTF8.GetBytes($"{Environment.ProcessId}\n");
            stream.Write(pid, 0, pid.Length);
            stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // diagnostics only; holding the lock is what matters
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;

        // the file is intentionally left in place: deleting it could remove the lock another
        // instance took in the moment between our close and the delete
    }
}
