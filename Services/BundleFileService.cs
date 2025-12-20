using mpvmux.Models;
using mpvmux.Models.DTO;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IBundleFileService
{
    Task LoadAsync(string path);
    Task NewFileAsync();
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

    public async Task SaveAsync()
    {
        UpdateBundleFromContext();
        await _videoRepositoryService.SaveAsync();
    }

    public async Task SaveAsAsync(string path)
    {
        UpdateBundleFromContext();
        await _videoRepositoryService.SaveAsAsync(path);
        await _historyService.SetLastDatabasePathAsync(path);
    }

    public async Task LoadAsync(string path)
    {
        await _videoRepositoryService.LoadFromFileAsync(path);
        _mediaContext.Bundle = MediaBundleModel.FromDto(_videoRepositoryService.Bundle);
        await _historyService.SetLastDatabasePathAsync(path);
    }

    public Task NewFileAsync()
    {
        _mediaContext.Bundle = new MediaBundleModel();
        _videoRepositoryService.UpdateBundle(new MediaModelBundleDTO());
        return Task.CompletedTask;
    }

    private void UpdateBundleFromContext()
    {
        var dto = _mediaContext.Bundle.ToDto();
        _videoRepositoryService.UpdateBundle(dto);
    }
}
