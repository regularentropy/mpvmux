using mpvmux.Constants;
using mpvmux.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IHistoryService
{
    HistoryModelDTO HistoryModel { get; }
    bool IsDirty { get; }
    Task AddEntryAsync(MediaRecord r);
    Task RemoveEntryAsync(MediaRecord r);
    string GetLastDatabasePath();
    Task SetLastDatabasePathAsync(string path);
    void SaveHistory();

    event EventHandler? OnHistoryChanged;
}

internal class HistoryService : IHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public HistoryModelDTO HistoryModel { get; private set; } = new();
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

    public async Task SetLastDatabasePathAsync(string path)
    {
        if (HistoryModel.LastDatabasePath == path)
            return;

        HistoryModel.LastDatabasePath = path;
        IsDirty = true;
    }

    public async Task AddEntryAsync(MediaRecord r)
    {
        if (HistoryModel.History.Any(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        HistoryModel.History.Add(r);
        IsDirty = true;
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task RemoveEntryAsync(MediaRecord r)
    {
        var recordToRemove = HistoryModel.History.FirstOrDefault(x => string.Equals(x.Path, r.Path, StringComparison.OrdinalIgnoreCase));
        if (recordToRemove is null)
            return Task.CompletedTask;

        HistoryModel.History.Remove(recordToRemove);

        if (HistoryModel.LastDatabasePath == recordToRemove.Path)
            HistoryModel.LastDatabasePath = null;

        IsDirty = true;

        SaveHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void LoadHistory()
    {
        try
        {
            using var fs = File.OpenRead(AppConstants.HistoryFilePath);
            HistoryModel = JsonSerializer.Deserialize<HistoryModelDTO>(fs) ?? new();
        }
        catch (JsonException)
        {
            HistoryModel = new();
            IsDirty = true;
        }

        var before = HistoryModel.History.Count;

        HistoryModel.History = HistoryModel.History
            .Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path))
            .ToList();

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