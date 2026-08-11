using mpvmux.Models.DTO;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IVideoRepositoryService
{
    MediaModelBundleDTO Bundle { get; }
    string SelectedFilePath { get; }

    Task SaveAsync();
    Task SaveAsAsync(string path);
    Task LoadFromFileAsync(string filePath);
    void UpdateBundle(MediaModelBundleDTO newBundle);

    event EventHandler<AddedVideoEventArgs> OnBundleChanged;
}

internal sealed class VideoRepositoryService : IVideoRepositoryService
{
    public MediaModelBundleDTO Bundle { get; private set; } = new();

    private string SelectedFileName { get; set; } = "";
    public string SelectedFilePath { get; private set; } = "";

    public event EventHandler<AddedVideoEventArgs>? OnBundleChanged;

    public async Task SaveAsync()
    {
        if (!string.IsNullOrEmpty(SelectedFilePath))
        {
            await SaveToFileAsync(Bundle, SelectedFilePath);
        }
    }

    public async Task SaveAsAsync(string path)
    {
        await SaveToFileAsync(Bundle, path);
    }

    private async Task SaveToFileAsync(MediaModelBundleDTO bundle, string path)
    {
        await using FileStream fs = new FileStream(path, FileMode.Create);
        await JsonSerializer.SerializeAsync(fs, bundle, new JsonSerializerOptions { WriteIndented = true });

        SelectedFilePath = path;
        SelectedFileName = Path.GetFileNameWithoutExtension(path);

        OnBundleChanged?.Invoke(this, new AddedVideoEventArgs(SelectedFileName, SelectedFilePath));
    }

    public async Task LoadFromFileAsync(string filePath)
    {
        await using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bundle = await JsonSerializer.DeserializeAsync<MediaModelBundleDTO>(fs);

        Bundle = bundle ?? new MediaModelBundleDTO();

        SelectedFilePath = filePath;
        SelectedFileName = Path.GetFileNameWithoutExtension(filePath);

        OnBundleChanged?.Invoke(this, new AddedVideoEventArgs(SelectedFileName, SelectedFilePath));
    }

    // a hack to update the bundle from the service
    public void UpdateBundle(MediaModelBundleDTO newBundle)
    {
        Bundle = newBundle;
    }
}

internal sealed class AddedVideoEventArgs(string key, string value) : EventArgs
{
    public string Key { get; init; } = key;
    public string Value { get; init; } = value;
}