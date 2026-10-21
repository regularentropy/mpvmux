using mpvmux.Constants;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// Recovery artifacts for bundles, kept in mpvmux's application data instead of beside the
/// user's bundles. A bundle path maps to a single backup slot and to quarantine names in the
/// corrupt folder, both keyed by the SHA-256 of the normalized path.
/// </summary>
internal interface IBundleBackupStore
{
    /// <summary>Stable slot key for a bundle path, independent of relative spelling and casing.</summary>
    string KeyFor(string bundlePath);

    bool Exists(string bundlePath);

    /// <summary>Writes the backup slot. Returns false when the store is not writable.</summary>
    Task<bool> WriteAsync(string bundlePath, string content);

    /// <summary>Reads and validates the backup slot.</summary>
    Task<BundleLoadResult> ReadAsync(string bundlePath);

    /// <summary>Raw backup text, or null when there is no backup.</summary>
    Task<string?> ReadTextAsync(string bundlePath);

    /// <summary>
    /// Moves a damaged bundle into the corrupt folder and returns its new path.
    /// </summary>
    string Quarantine(string bundlePath);
}

internal sealed class BundleBackupStore : IBundleBackupStore
{
    private readonly string _backupFolderPath;
    private readonly string _corruptFolderPath;

    public BundleBackupStore()
        : this(AppConstants.BackupFolderPath, AppConstants.CorruptFolderPath)
    {
    }

    public BundleBackupStore(string backupFolderPath, string corruptFolderPath)
    {
        _backupFolderPath = backupFolderPath;
        _corruptFolderPath = corruptFolderPath;
    }

    /// <summary>Where builds before the store lived kept their backup: beside the bundle.</summary>
    public static string LegacyBackupPathFor(string bundlePath) => bundlePath + ".bak";

    public string BackupPathFor(string bundlePath) =>
        Path.Combine(_backupFolderPath, KeyFor(bundlePath) + ".mpvm.bak");

    public string KeyFor(string bundlePath)
    {
        var normalized = Path.GetFullPath(bundlePath);

        // case-insensitive file systems must not produce a new slot for a different spelling
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool Exists(string bundlePath) => File.Exists(BackupPathFor(bundlePath));

    public async Task<bool> WriteAsync(string bundlePath, string content)
    {
        try
        {
            Directory.CreateDirectory(_backupFolderPath);
            await AtomicFile.WriteAllTextAsync(BackupPathFor(bundlePath), content);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public Task<BundleLoadResult> ReadAsync(string bundlePath) =>
        BundleSerializer.LoadAsync(BackupPathFor(bundlePath));

    public async Task<string?> ReadTextAsync(string bundlePath)
    {
        var backupPath = BackupPathFor(bundlePath);
        if (!File.Exists(backupPath))
            return null;

        try
        {
            return await File.ReadAllTextAsync(backupPath);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public string Quarantine(string bundlePath)
    {
        Directory.CreateDirectory(_corruptFolderPath);

        var key = KeyFor(bundlePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(_corruptFolderPath, $"{key}-{stamp}.mpvm.corrupt");
        var suffix = 1;

        while (File.Exists(destination))
            destination = Path.Combine(_corruptFolderPath, $"{key}-{stamp}-{suffix++}.mpvm.corrupt");

        File.Move(bundlePath, destination);
        return destination;
    }
}
