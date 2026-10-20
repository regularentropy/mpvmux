using mpvmux.Models;
using System;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleFileService
{
    Task LoadAsync(string path);
    Task NewFileAsync();
    Task SaveAsAsync(string path);
    Task SaveAsync();

    event EventHandler<AddedVideoEventArgs>? OnBundleChanged;
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

        _videoRepositoryService.OnBundleChanged += (_, e) => OnBundleChanged?.Invoke(this, e);
    }

    public event EventHandler<AddedVideoEventArgs>? OnBundleChanged;

    public Task SaveAsync() => _videoRepositoryService.SaveAsync(_mediaContext.Bundle);

    public async Task SaveAsAsync(string path)
    {
        await _videoRepositoryService.SaveAsAsync(_mediaContext.Bundle, path);
        await _historyService.SetLastDatabasePathAsync(path);
    }

    public async Task LoadAsync(string path)
    {
        _mediaContext.Bundle = await _videoRepositoryService.LoadFromFileAsync(path);
        await _historyService.SetLastDatabasePathAsync(path);
    }

    public Task NewFileAsync()
    {
        _mediaContext.Bundle = new MediaBundleModel();
        _videoRepositoryService.ClearSelectedFile();
        return Task.CompletedTask;
    }
}
