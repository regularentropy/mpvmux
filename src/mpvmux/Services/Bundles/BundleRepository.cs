using mpvmux.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// Outcome of a save. A warning means the bundle itself was written but recovery state could
/// not be updated, so the caller should report it without treating the save as failed.
/// </summary>
internal sealed record BundleSaveResult(string? Warning = null)
{
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}

/// <summary>
/// Whether a save advances the bundle's recovery slot. Autosave uses <see cref="Keep"/> so the
/// previous version stays the last deliberate save instead of a state from a few seconds ago.
/// </summary>
internal enum BundleBackupMode
{
    Promote,
    Keep,
}

internal interface IBundleRepository
{
    string SelectedFilePath { get; }

    event EventHandler? SelectedFilePathChanged;

    Task<BundleSaveResult> SaveAsync(MediaBundleModel bundle, BundleBackupMode backupMode = BundleBackupMode.Promote);

    Task<BundleSaveResult> SaveAsAsync(MediaBundleModel bundle, string path);

    /// <summary>
    /// Reads and validates a bundle. A failed result leaves <see cref="SelectedFilePath"/> and
    /// the caller's session untouched.
    /// </summary>
    Task<BundleLoadResult> LoadFromFileAsync(string filePath);

    /// <summary>
    /// Restores the stored backup over a corrupt bundle after moving the corrupt file into the
    /// backup store's corrupt folder. Fails when the backup is missing or does not validate.
    /// </summary>
    Task<BundleLoadResult> RestoreBackupAsync(string filePath);

    void ClearSelectedFile();
}

internal sealed class BundleRepository : IBundleRepository
{
    private readonly IBundleBackupStore _backupStore;
    private readonly IBundleEditTracker? _editTracker;
    private string _selectedFilePath = "";

    // saves can be triggered from commands and from shutdown; they must not interleave
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public BundleRepository(IBundleBackupStore backupStore, IBundleEditTracker? editTracker = null)
    {
        _backupStore = backupStore;
        _editTracker = editTracker;
    }

    public string SelectedFilePath => _selectedFilePath;

    public event EventHandler? SelectedFilePathChanged;

    public async Task<BundleSaveResult> SaveAsync(
        MediaBundleModel bundle,
        BundleBackupMode backupMode = BundleBackupMode.Promote)
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
            return new BundleSaveResult();

        return await SaveToFileAsync(bundle, SelectedFilePath, backupMode);
    }

    public Task<BundleSaveResult> SaveAsAsync(MediaBundleModel bundle, string path) =>
        SaveToFileAsync(bundle, path, BundleBackupMode.Promote);

    private async Task<BundleSaveResult> SaveToFileAsync(
        MediaBundleModel bundle,
        string path,
        BundleBackupMode backupMode)
    {
        await _saveLock.WaitAsync();
        try
        {
            var existing = await BundleSerializer.LoadAsync(path);

            // never let an older build flatten a file written by a newer one
            if (existing.Error == BundleLoadError.NewerSchema)
                throw new BundleSaveException(existing.Message);

            // fingerprints are refreshed on every save so a later rename can be recognised;
            // they are additive fields, so the schema version stays as it is. The capture
            // replaces records in place, which is not a user edit.
            using (_editTracker?.Suspend())
            {
                MediaFileFingerprint.Capture(bundle);
            }

            var json = BundleSerializer.Serialize(bundle);
            string? warning = null;

            // Only promote a file that still parses. When the current file is already corrupt,
            // leaving the older backup alone gives the user something to restore. Autosave keeps
            // the slot so it still holds the last deliberate save.
            if (existing.IsSuccess && backupMode == BundleBackupMode.Promote)
            {
                var current = await File.ReadAllTextAsync(path);
                warning = await UpdateBackupAsync(path, current);
            }

            await AtomicFile.WriteAllTextAsync(path, json);
            SetSelectedFilePath(path);
            return new BundleSaveResult(warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BundleSaveException($"Could not write '{path}': {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            throw new BundleSaveException($"Could not write '{path}': {ex.Message}", ex);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Stores the last good content. A legacy sidecar is imported once, before the store has
    /// an entry for this path, and left beside the bundle untouched. Returns a warning when the
    /// backup could not be written; the bundle save itself still succeeds.
    /// </summary>
    private async Task<string?> UpdateBackupAsync(string path, string currentContent)
    {
        var content = currentContent;

        if (!_backupStore.Exists(path))
            content = await ReadLegacyBackupTextAsync(path) ?? content;

        return await _backupStore.WriteAsync(path, content)
            ? null
            : "Bundle saved, but the backup could not be updated: the backup file could not be written.";
    }

    private static async Task<string?> ReadLegacyBackupTextAsync(string path)
    {
        var legacyPath = BundleBackupStore.LegacyBackupPathFor(path);

        try
        {
            if (!File.Exists(legacyPath) || !await BundleSerializer.IsValidFileAsync(legacyPath))
                return null;

            return await File.ReadAllTextAsync(legacyPath);
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

    public async Task<BundleLoadResult> LoadFromFileAsync(string filePath)
    {
        // a crash during an earlier save may have left this behind
        AtomicFile.CleanupTemp(filePath);

        var result = await BundleSerializer.LoadAsync(filePath);
        if (result.IsSuccess)
        {
            SetSelectedFilePath(filePath);
            return result;
        }

        return result with { CanRestoreBackup = await CanRestoreBackupAsync(filePath) };
    }

    private async Task<bool> CanRestoreBackupAsync(string filePath)
    {
        if (_backupStore.Exists(filePath))
            return (await _backupStore.ReadAsync(filePath)).IsSuccess;

        return await ReadLegacyBackupTextAsync(filePath) is not null;
    }

    public async Task<BundleLoadResult> RestoreBackupAsync(string filePath)
    {
        await _saveLock.WaitAsync();
        try
        {
            var backup = await ReadRestorableBackupAsync(filePath);
            if (!backup.IsSuccess || backup.Bundle is null)
                return backup with { CanRestoreBackup = false };

            var restored = await ReadRestoredTextAsync(filePath);
            if (restored is null)
                return BundleLoadResult.Failure(
                    BundleLoadError.Unreadable,
                    "The backup could not be read.");

            // keep the corrupt original for inspection instead of destroying evidence
            if (File.Exists(filePath))
                _backupStore.Quarantine(filePath);

            await AtomicFile.WriteAllTextAsync(filePath, restored);
            SetSelectedFilePath(filePath);
            return backup;
        }
        catch (UnauthorizedAccessException ex)
        {
            return BundleLoadResult.Failure(
                BundleLoadError.Unreadable,
                $"The backup could not be restored: {ex.Message}");
        }
        catch (IOException ex)
        {
            return BundleLoadResult.Failure(
                BundleLoadError.Unreadable,
                $"The backup could not be restored: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// The stored backup, or a valid legacy sidecar when the store has no entry. A legacy
    /// sidecar is imported into the store before the bundle is overwritten.
    /// </summary>
    private async Task<BundleLoadResult> ReadRestorableBackupAsync(string filePath)
    {
        if (_backupStore.Exists(filePath))
            return await _backupStore.ReadAsync(filePath);

        var legacyPath = BundleBackupStore.LegacyBackupPathFor(filePath);
        if (!File.Exists(legacyPath))
            return BundleLoadResult.Failure(
                BundleLoadError.Missing,
                "There is no backup for this bundle.");

        var legacy = await BundleSerializer.LoadAsync(legacyPath);
        if (legacy.IsSuccess)
        {
            var text = await File.ReadAllTextAsync(legacyPath);
            await _backupStore.WriteAsync(filePath, text);
        }

        return legacy;
    }

    private async Task<string?> ReadRestoredTextAsync(string filePath)
    {
        var stored = await _backupStore.ReadTextAsync(filePath);
        if (stored is not null)
            return stored;

        // the store write during import may have failed; the validated legacy sidecar is still
        // readable and restore must not silently do nothing in that case
        var legacyPath = BundleBackupStore.LegacyBackupPathFor(filePath);
        return File.Exists(legacyPath) ? await File.ReadAllTextAsync(legacyPath) : null;
    }

    public void ClearSelectedFile()
    {
        SetSelectedFilePath("");
    }

    private void SetSelectedFilePath(string path)
    {
        if (string.Equals(_selectedFilePath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _selectedFilePath = path;
        SelectedFilePathChanged?.Invoke(this, EventArgs.Empty);
    }
}
