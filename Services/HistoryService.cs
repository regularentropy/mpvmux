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
    Task AddEntryAsync(MediaRecord r);
    Task RemoveEntryAsync(MediaRecord r);
    string GetLastDatabasePath();
    Task SetLastDatabasePathAsync(string path);

    event EventHandler OnHistoryChanged;
}

internal class HistoryService : IHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public HistoryModelDTO HistoryModel { get; private set; } = new();
    public event EventHandler? OnHistoryChanged;

    public HistoryService()
    {
        if (!File.Exists(AppConstants.HistoryFilePath))
        {
            SaveHistorySync();
            OnHistoryChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        LoadHistory();
        CleanHistory();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddEntryAsync(MediaRecord r)
    {
        if (HistoryModel.History.Any(x => x.Name == r.Name))
            return;

        HistoryModel.History.Add(r);
        await SaveHistoryAsync();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveEntryAsync(MediaRecord r)
    {
        var recordToRemove = HistoryModel.History.FirstOrDefault(x => x.Name == r.Name);
        if (recordToRemove is null)
            return;

        HistoryModel.History.Remove(recordToRemove);
        await SaveHistoryAsync();
        OnHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetLastDatabasePathAsync(string path)
    {
        HistoryModel.LastDatabasePath = path;
        await SaveHistoryAsync();
    }

    public string GetLastDatabasePath()
    {
        return HistoryModel.LastDatabasePath ?? "";
    }

    private void LoadHistory()
    {
        using var fs = File.OpenRead(AppConstants.HistoryFilePath);
        HistoryModel = JsonSerializer.Deserialize<HistoryModelDTO>(fs) ?? new();
    }

    private void CleanHistory()
    {
        var originalCount = HistoryModel.History.Count;
        HistoryModel.History = HistoryModel.History.Where(x => File.Exists(x.Path)).ToList();

        if (originalCount != HistoryModel.History.Count)
            SaveHistorySync();
    }

    private async Task SaveHistoryAsync()
    {
        await using var fs = File.Create(AppConstants.HistoryFilePath);
        await JsonSerializer.SerializeAsync(fs, HistoryModel, JsonOptions);
    }

    private void SaveHistorySync()
    {
        using var fs = File.Create(AppConstants.HistoryFilePath);
        JsonSerializer.Serialize(fs, HistoryModel, JsonOptions);
    }
}