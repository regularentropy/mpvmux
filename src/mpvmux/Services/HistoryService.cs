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
        if (HistoryModel.History.Any(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        HistoryModel.History.Add(r);
        IsDirty = true;
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveEntry(MediaRecord r)
    {
        var recordToRemove = HistoryModel.History.FirstOrDefault(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase));
        if (recordToRemove is null)
            return;

        HistoryModel.History.Remove(recordToRemove);

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

        var before = HistoryModel.History.Count;

        HistoryModel.History = new ObservableCollection<MediaRecord>(
            HistoryModel.History.Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path)));

        if (HistoryModel.History.Count != before)
            IsDirty = true;
    }

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