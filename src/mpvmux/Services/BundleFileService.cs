using mpvmux.Models;
using System.IO;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleFileService
{
    Task LoadAsync(string path);
    void NewFile();
    Task SaveAsAsync(string path);
    Task SaveAsync();
}

internal class BundleFileService : IBundleFileService
{
    private readonly IVideoRepositoryService _videoRepositoryService;
    private readonly MediaContext _mediaContext;
    private readonly IHistoryService _historyService;

    public BundleFileService(
        IVideoRepositoryService videoRepositoryService,
        MediaContext mediaContext,
        IHistoryService historyService)
    {
        _videoRepositoryService = videoRepositoryService;
        _mediaContext = mediaContext;
        _historyService = historyService;
    }

    public Task SaveAsync() => _videoRepositoryService.SaveAsync(_mediaContext.Bundle);

    public async Task SaveAsAsync(string path)
    {
        await _videoRepositoryService.SaveAsAsync(_mediaContext.Bundle, path);
        Remember(path);
        _historyService.SetLastDatabasePath(path);
    }

    public async Task LoadAsync(string path)
    {
        _mediaContext.Bundle = await _videoRepositoryService.LoadFromFileAsync(path);
        // opening a .mpvm the user has not opened before adds it to history; repeats are a no-op
        Remember(path);
        _historyService.SetLastDatabasePath(path);
    }

    public void NewFile()
    {
        _mediaContext.Bundle = new MediaBundleModel();
        _videoRepositoryService.ClearSelectedFile();
    }

    private void Remember(string path) =>
        _historyService.AddEntry(new MediaRecord(Path.GetFileNameWithoutExtension(path), path));
}
