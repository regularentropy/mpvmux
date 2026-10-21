using mpvmux.Constants;
using mpvmux.Models;
using System.Collections.ObjectModel;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace mpvmux.Services;

internal interface IHistoryService
{
    HistoryModel HistoryModel { get; }
    bool IsDirty { get; }
    void AddEntry(MediaRecord r);
    void RemoveEntry(MediaRecord r);
    void RenameEntry(MediaRecord record, string name);
    void TogglePin(MediaRecord record);

    /// <summary>
    /// Re-checks every entry against the file system and updates their missing flag.
    /// Returns true when at least one entry changed and listeners were notified.
    /// </summary>
    bool RefreshAvailability();

    /// <summary>
    /// Points an entry at another file, keeping its pin, name and position. Completes with
    /// false when the record is not in history.
    /// </summary>
    bool RelinkEntry(MediaRecord record, string newPath);

    /// <summary>Removes every entry whose file is missing. Returns the number removed.</summary>
    int RemoveObsoleteEntries();

    /// <summary>
    /// Moves <paramref name="record"/> directly before or after <paramref name="target"/>.
    /// Both records must be in the same pin group; crossing the pinned/recent boundary is
    /// rejected because pinning is the operation that changes a record's group.
    /// </summary>
    bool MoveEntry(MediaRecord record, MediaRecord target, bool placeAfter);

    /// <summary>Moves a record to the top of its own group.</summary>
    bool PromoteEntry(MediaRecord record);

    string GetLastDatabasePath();
    void SetLastDatabasePath(string path);
    void SaveHistory();

    event EventHandler? OnHistoryChanged;
}

internal class HistoryService : IHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public HistoryModel HistoryModel { get; private set; } = new();
    public bool IsDirty { get; private set; }
    public event EventHandler? OnHistoryChanged;

    public HistoryService()
    {
        if (!File.Exists(AppConstants.HistoryFilePath))
        {
            WriteHistory();
            return;
        }

        LoadHistory();
    }

    public string GetLastDatabasePath()
    {
        var last = HistoryModel.LastDatabasePath;

        if (!string.IsNullOrWhiteSpace(last) && File.Exists(last))
            return last;

        var fallback = HistoryModel.History.FirstOrDefault(x => File.Exists(x.Path));
        if (fallback is null)
            return string.Empty;

        HistoryModel.LastDatabasePath = fallback.Path;
        IsDirty = true;
        return fallback.Path;
    }

    public void SetLastDatabasePath(string path)
    {
        if (HistoryModel.LastDatabasePath == path)
            return;

        HistoryModel.LastDatabasePath = path;
        IsDirty = true;
    }

    public void AddEntry(MediaRecord r)
    {
        var index = IndexOfPath(r.Path);

        if (index >= 0)
        {
            // pinned entries are curated by hand, so opening one must not disturb their order
            if (HistoryModel.History[index].IsPinned)
                return;

            // opening a recent bundle promotes it to the top of the recent group
            var firstRecent = FirstRecentIndex();

            if (firstRecent < 0 || firstRecent == index)
                return;

            HistoryModel.History.Move(index, firstRecent);
        }
        else
        {
            // new bundles go directly below the pinned ones, newest first
            var firstRecent = FirstRecentIndex();

            HistoryModel.History.Insert(firstRecent < 0 ? HistoryModel.History.Count : firstRecent, r);
        }

        IsDirty = true;
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private int IndexOfPath(string path)
    {
        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            if (string.Equals(HistoryModel.History[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Index of the first unpinned entry. Pinned entries always form the prefix of the list,
    /// so this is also the index of the top of the recent group; -1 when everything is pinned.
    /// </summary>
    private int FirstRecentIndex()
    {
        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            if (!HistoryModel.History[i].IsPinned)
                return i;
        }

        return -1;
    }

    public bool MoveEntry(MediaRecord record, MediaRecord target, bool placeAfter)
    {
        var source = IndexOfPath(record.Path);
        var targetIndex = IndexOfPath(target.Path);

        if (source < 0 || targetIndex < 0 || source == targetIndex)
            return false;

        var moving = HistoryModel.History[source];
        if (moving.IsPinned != HistoryModel.History[targetIndex].IsPinned)
            return false;

        // destination in terms of the list after the source has been removed
        var desired = placeAfter ? targetIndex + 1 : targetIndex;
        if (desired == source || desired == source + 1)
            return false;

        HistoryModel.History.RemoveAt(source);
        if (desired > source)
            desired--;

        HistoryModel.History.Insert(desired, moving);

        IsDirty = true;
        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool PromoteEntry(MediaRecord record)
    {
        var index = IndexOfPath(record.Path);
        if (index < 0)
            return false;

        var moving = HistoryModel.History[index];
        var groupStart = moving.IsPinned ? 0 : FirstRecentIndex();

        if (groupStart < 0 || groupStart == index)
            return false;

        HistoryModel.History.Move(index, groupStart);

        IsDirty = true;
        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RenameEntry(MediaRecord record, string name)
    {
        var index = IndexOfPath(record.Path);
        var newName = name.Trim();

        if (index < 0 || string.IsNullOrEmpty(newName) || string.Equals(HistoryModel.History[index].Name, newName, StringComparison.Ordinal))
            return;

        // only the display name changes; the bundle file keeps its path and filename
        HistoryModel.History[index] = HistoryModel.History[index] with { Name = newName };
        IsDirty = true;

        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePin(MediaRecord record)
    {
        var index = IndexOfPath(record.Path);
        if (index < 0)
            return;

        var stored = HistoryModel.History[index];
        var isPinned = !stored.IsPinned;

        if (isPinned)
        {
            if (!IsPinnedPath(stored.Path))
                HistoryModel.PinnedPaths.Add(stored.Path);
        }
        else
        {
            HistoryModel.PinnedPaths.RemoveAll(p => string.Equals(p, stored.Path, StringComparison.OrdinalIgnoreCase));
        }

        // Re-insert instead of replacing in place: pinning puts the entry at the top of the
        // pinned group, unpinning at the top of recent, keeping the pinned prefix invariant.
        HistoryModel.History.RemoveAt(index);

        var target = isPinned ? 0 : FirstRecentIndex();
        HistoryModel.History.Insert(target < 0 ? HistoryModel.History.Count : target, stored with { IsPinned = isPinned });

        IsDirty = true;

        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveEntry(MediaRecord r)
    {
        var recordToRemove = HistoryModel.History.FirstOrDefault(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase));
        if (recordToRemove is null)
            return;

        HistoryModel.History.Remove(recordToRemove);
        HistoryModel.PinnedPaths.RemoveAll(p => string.Equals(p, recordToRemove.Path, StringComparison.OrdinalIgnoreCase));

        if (HistoryModel.LastDatabasePath == recordToRemove.Path)
            HistoryModel.LastDatabasePath = null;

        IsDirty = true;

        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RelinkEntry(MediaRecord record, string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
            return false;

        var source = IndexOfPath(record.Path);
        if (source < 0)
            return false;

        var isMissing = !File.Exists(newPath);
        var target = IndexOfPath(newPath);

        if (target == source)
        {
            var current = HistoryModel.History[source];
            if (current.IsMissing == isMissing)
                return false;

            HistoryModel.History[source] = current with { IsMissing = isMissing };
            IsDirty = true;
            SaveHistory();
            OnHistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var moving = HistoryModel.History[source];

        if (target >= 0)
        {
            // The new path is already a history entry: fold both into one record instead of
            // leaving a duplicate. The existing record keeps its curated name and position
            // class; a pin from either side wins.
            var existing = HistoryModel.History[target];
            var merged = existing with
            {
                IsPinned = existing.IsPinned || moving.IsPinned,
                IsMissing = isMissing
            };

            HistoryModel.History.RemoveAt(source);
            HistoryModel.History.RemoveAt(IndexOfPath(newPath));
            InsertGrouped(merged);
        }
        else
        {
            // Keep the entry in place: its name, pin and position are user state.
            HistoryModel.History[source] = moving with { Path = newPath, IsMissing = isMissing };
        }

        UpdatePinnedPath(record.Path, newPath, IsPinnedPath(record.Path) || IsPinnedPath(newPath));

        if (string.Equals(HistoryModel.LastDatabasePath, record.Path, StringComparison.OrdinalIgnoreCase))
            HistoryModel.LastDatabasePath = newPath;

        IsDirty = true;
        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public int RemoveObsoleteEntries()
    {
        // A file that reappeared since the last check must not be removed.
        RefreshAvailability();

        var removed = 0;

        for (var i = HistoryModel.History.Count - 1; i >= 0; i--)
        {
            var record = HistoryModel.History[i];
            if (!record.IsMissing)
                continue;

            HistoryModel.History.RemoveAt(i);
            HistoryModel.PinnedPaths.RemoveAll(path =>
                string.Equals(path, record.Path, StringComparison.OrdinalIgnoreCase));

            if (string.Equals(HistoryModel.LastDatabasePath, record.Path, StringComparison.OrdinalIgnoreCase))
                HistoryModel.LastDatabasePath = null;

            removed++;
        }

        if (removed == 0)
            return 0;

        IsDirty = true;
        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    /// <summary>Inserts a record at the top of its own group, keeping the pinned prefix invariant.</summary>
    private void InsertGrouped(MediaRecord record)
    {
        var index = record.IsPinned ? 0 : FirstRecentIndex();
        HistoryModel.History.Insert(index < 0 ? HistoryModel.History.Count : index, record);
    }

    private void UpdatePinnedPath(string oldPath, string newPath, bool isPinned)
    {
        HistoryModel.PinnedPaths.RemoveAll(path =>
            string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase));

        if (isPinned)
            HistoryModel.PinnedPaths.Add(newPath);
    }

    private void LoadHistory()
    {
        try
        {
            using var fs = File.OpenRead(AppConstants.HistoryFilePath);
            HistoryModel = JsonSerializer.Deserialize<HistoryModel>(fs) ?? new();
        }
        catch (JsonException)
        {
            HistoryModel = new();
            IsDirty = true;
        }

        HistoryModel.PinnedPaths ??= [];

        var previousOrder = HistoryModel.History.Select(x => x.Path).ToArray();

        // Entries whose file is gone are kept and marked obsolete: the Manage window lets the
        // user re-link or remove them deliberately instead of silently dropping the record.
        var loaded = HistoryModel.History
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .Select(x => x with
            {
                IsPinned = IsPinnedPath(x.Path),
                IsMissing = !File.Exists(x.Path)
            })
            .ToList();

        // Normalise to the invariant the rest of the service relies on: pinned entries form
        // the prefix, each group keeps its persisted order. The list order is the display order.
        var ordered = loaded.OrderByDescending(x => x.IsPinned).ToList();
        HistoryModel.History = new ObservableCollection<MediaRecord>(ordered);

        // drop pins that no longer belong to any record (blank paths can still be discarded)
        var pinnedBefore = HistoryModel.PinnedPaths.Count;
        HistoryModel.PinnedPaths.RemoveAll(path =>
            !HistoryModel.History.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)));

        var orderChanged = !previousOrder.SequenceEqual(
            ordered.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);

        if (orderChanged || HistoryModel.PinnedPaths.Count != pinnedBefore)
            IsDirty = true;
    }

    public bool RefreshAvailability()
    {
        var changed = false;

        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            var record = HistoryModel.History[i];
            var isMissing = string.IsNullOrWhiteSpace(record.Path) || !File.Exists(record.Path);

            if (record.IsMissing == isMissing)
                continue;

            HistoryModel.History[i] = record with { IsMissing = isMissing };
            changed = true;
        }

        if (changed)
            OnHistoryChanged?.Invoke(this, EventArgs.Empty);

        return changed;
    }

    private bool IsPinnedPath(string path) =>
        HistoryModel.PinnedPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public void SaveHistory()
    {
        if (!IsDirty)
            return;

        WriteHistory();
    }

    private void WriteHistory()
    {
        using var fs = File.Create(AppConstants.HistoryFilePath);
        JsonSerializer.Serialize(fs, HistoryModel, JsonOptions);
        IsDirty = false;
    }
}