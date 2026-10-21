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
            // opening a bundle that is already listed promotes it to the top of its group,
            // so the dropdown reflects most-recently-used order
            var record = HistoryModel.History[index];
            var first = FirstOfGroup(record, index);

            if (first < 0 || first == index + 1)
                return;

            // Move removes the item first, which shifts a later target left by one
            HistoryModel.History.Move(index, first < index ? first : first - 1);
        }
        else
        {
            // new bundles go below the pinned ones, newest first
            var first = FirstOfGroup(r, -1);

            HistoryModel.History.Insert(first < 0 ? HistoryModel.History.Count : first, r);
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
    /// Index of the first entry in the same display group (pinned or unpinned), ignoring
    /// <paramref name="excludeIndex"/>; -1 when the record would be the group's only member.
    /// </summary>
    private int FirstOfGroup(MediaRecord record, int excludeIndex)
    {
        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            if (i != excludeIndex && HistoryModel.History[i].IsPinned == record.IsPinned)
                return i;
        }

        return -1;
    }

    public void RenameEntry(MediaRecord record, string name)
    {
        var index = HistoryModel.History.IndexOf(record);
        var newName = name.Trim();

        if (index < 0 || string.IsNullOrEmpty(newName) || string.Equals(record.Name, newName, StringComparison.Ordinal))
            return;

        // only the display name changes; the bundle file keeps its path and filename
        HistoryModel.History[index] = record with { Name = newName };
        IsDirty = true;

        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePin(MediaRecord record)
    {
        var index = HistoryModel.History.IndexOf(record);
        if (index < 0)
            return;

        var isPinned = !HistoryModel.History[index].IsPinned;

        if (isPinned)
        {
            if (!IsPinnedPath(record.Path))
                HistoryModel.PinnedPaths.Add(record.Path);
        }
        else
        {
            HistoryModel.PinnedPaths.RemoveAll(p => string.Equals(p, record.Path, StringComparison.OrdinalIgnoreCase));
        }

        HistoryModel.History[index] = record with { IsPinned = isPinned };
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

        var before = HistoryModel.History.Count;

        HistoryModel.History = new ObservableCollection<MediaRecord>(
            HistoryModel.History
                .Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path))
                .Select(x => x with { IsPinned = IsPinnedPath(x.Path) }));

        // drop pins for entries that are no longer in the recent list
        var pinnedBefore = HistoryModel.PinnedPaths.Count;
        HistoryModel.PinnedPaths.RemoveAll(path =>
            !HistoryModel.History.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)));

        if (HistoryModel.History.Count != before || HistoryModel.PinnedPaths.Count != pinnedBefore)
            IsDirty = true;
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