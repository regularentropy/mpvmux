using mpvmux.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleRepository
{
    string SelectedFilePath { get; }

    event EventHandler? SelectedFilePathChanged;

    Task SaveAsync(MediaBundleModel bundle);
    Task SaveAsAsync(MediaBundleModel bundle, string path);
    Task<MediaBundleModel> LoadFromFileAsync(string filePath);
    void ClearSelectedFile();
}

internal sealed class BundleRepository : IBundleRepository
{
    private string _selectedFilePath = "";

    public string SelectedFilePath => _selectedFilePath;

    public event EventHandler? SelectedFilePathChanged;

    public async Task SaveAsync(MediaBundleModel bundle)
    {
        if (!string.IsNullOrEmpty(SelectedFilePath))
        {
            await SaveToFileAsync(bundle, SelectedFilePath);
        }
    }

    public async Task SaveAsAsync(MediaBundleModel bundle, string path)
    {
        await SaveToFileAsync(bundle, path);
    }

    private async Task SaveToFileAsync(MediaBundleModel bundle, string path)
    {
        await using FileStream fs = new FileStream(path, FileMode.Create);
        await JsonSerializer.SerializeAsync(fs, bundle, new JsonSerializerOptions { WriteIndented = true });

        SetSelectedFilePath(path);
    }

    public async Task<MediaBundleModel> LoadFromFileAsync(string filePath)
    {
        await using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bundle = await JsonSerializer.DeserializeAsync<MediaBundleModel>(fs) ?? new MediaBundleModel();

        SetSelectedFilePath(filePath);

        return bundle;
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