using mpvmux.Constants;
using System;
using System.IO;

namespace mpvmux.Services;

/// <summary>
/// Recovery artifacts for mpvmux's own JSON files (<c>config.json</c>, <c>history.json</c>),
/// laid out like the bundle store: one fixed backup slot per file under <c>backups/</c> and
/// timestamped copies of damaged files under <c>corrupt/</c>. Both live in the application data
/// folder, so a user's settings never gain sidecar files next to their media.
/// </summary>
internal sealed class SettingsBackupStore
{
    private readonly string _backupFolderPath;
    private readonly string _corruptFolderPath;

    public SettingsBackupStore(string backupFolderPath, string corruptFolderPath)
    {
        _backupFolderPath = backupFolderPath;
        _corruptFolderPath = corruptFolderPath;
    }

    /// <summary>
    /// Backups and quarantine live beside the settings file they protect, under the folder
    /// names declared in <see cref="AppConstants"/>.
    /// </summary>
    public static SettingsBackupStore ForFile(string settingsFilePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(settingsFilePath)) ?? "";

        return new SettingsBackupStore(
            Path.Combine(folder, Path.GetFileName(AppConstants.BackupFolderPath)),
            Path.Combine(folder, Path.GetFileName(AppConstants.CorruptFolderPath)));
    }

    public string BackupPathFor(string fileName) => Path.Combine(_backupFolderPath, fileName + ".bak");

    /// <summary>Raw backup text, or null when the slot is missing or unreadable.</summary>
    public string? Read(string fileName)
    {
        var backupPath = BackupPathFor(fileName);

        try
        {
            return File.Exists(backupPath) ? File.ReadAllText(backupPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the slot atomically. Returns false when the store is not writable.</summary>
    public bool Write(string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(_backupFolderPath);
            AtomicFile.WriteAllText(BackupPathFor(fileName), content);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves a damaged file into the corrupt folder and returns its new path. Returns null when
    /// it could not be moved, for example because it is locked or is not a file at all; the
    /// caller then leaves it alone rather than destroying the only copy.
    /// </summary>
    public string? Quarantine(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            Directory.CreateDirectory(_corruptFolderPath);

            var fileName = Path.GetFileName(filePath);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

            var destination = Path.Combine(_corruptFolderPath, $"{stem}-{stamp}{extension}.corrupt");
            var suffix = 1;

            while (File.Exists(destination))
                destination = Path.Combine(_corruptFolderPath, $"{stem}-{stamp}-{suffix++}{extension}.corrupt");

            File.Move(filePath, destination);
            return destination;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
