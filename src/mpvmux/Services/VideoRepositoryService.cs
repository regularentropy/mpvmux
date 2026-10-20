using mpvmux.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IVideoRepositoryService
{
    string SelectedFilePath { get; }

    Task SaveAsync(MediaBundleModel bundle);
    Task SaveAsAsync(MediaBundleModel bundle, string path);
    Task<MediaBundleModel> LoadFromFileAsync(string filePath);
    void ClearSelectedFile();

    event EventHandler<AddedVideoEventArgs> OnBundleChanged;
}

internal sealed class VideoRepositoryService : IVideoRepositoryService
{
    private string SelectedFileName { get; set; } = "";
    public string SelectedFilePath { get; private set; } = "";

    public event EventHandler<AddedVideoEventArgs>? OnBundleChanged;

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

        SelectedFilePath = path;
        SelectedFileName = Path.GetFileNameWithoutExtension(path);

        OnBundleChanged?.Invoke(this, new AddedVideoEventArgs(SelectedFileName, SelectedFilePath));
    }

    public async Task<MediaBundleModel> LoadFromFileAsync(string filePath)
    {
        await using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bundle = await JsonSerializer.DeserializeAsync<MediaBundleModel>(fs) ?? new MediaBundleModel();

        SelectedFilePath = filePath;
        SelectedFileName = Path.GetFileNameWithoutExtension(filePath);

        OnBundleChanged?.Invoke(this, new AddedVideoEventArgs(SelectedFileName, SelectedFilePath));

        return bundle;
    }

    public void ClearSelectedFile()
    {
        SelectedFilePath = "";
        SelectedFileName = "";
    }
}

internal sealed class AddedVideoEventArgs(string key, string value) : EventArgs
{
    public string Key { get; init; } = key;
    public string Value { get; init; } = value;
}