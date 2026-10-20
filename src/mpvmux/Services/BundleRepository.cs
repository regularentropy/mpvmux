using mpvmux.Models;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleRepository
{
    string SelectedFilePath { get; }

    Task SaveAsync(MediaBundleModel bundle);
    Task SaveAsAsync(MediaBundleModel bundle, string path);
    Task<MediaBundleModel> LoadFromFileAsync(string filePath);
    void ClearSelectedFile();
}

internal sealed class BundleRepository : IBundleRepository
{
    public string SelectedFilePath { get; private set; } = "";

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
    }

    public async Task<MediaBundleModel> LoadFromFileAsync(string filePath)
    {
        await using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var bundle = await JsonSerializer.DeserializeAsync<MediaBundleModel>(fs) ?? new MediaBundleModel();

        SelectedFilePath = filePath;

        return bundle;
    }

    public void ClearSelectedFile()
    {
        SelectedFilePath = "";
    }
}