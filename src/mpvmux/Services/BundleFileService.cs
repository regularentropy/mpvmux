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
    private readonly IBundleRepository _bundleRepository;
    private readonly BundleSession _bundleSession;
    private readonly IHistoryService _historyService;

    public BundleFileService(
        IBundleRepository bundleRepository,
        BundleSession bundleSession,
        IHistoryService historyService)
    {
        _bundleRepository = bundleRepository;
        _bundleSession = bundleSession;
        _historyService = historyService;
    }

    public Task SaveAsync() => _bundleRepository.SaveAsync(_bundleSession.Bundle);

    public async Task SaveAsAsync(string path)
    {
        await _bundleRepository.SaveAsAsync(_bundleSession.Bundle, path);
        Remember(path);
        _historyService.SetLastDatabasePath(path);
    }

    public async Task LoadAsync(string path)
    {
        _bundleSession.Bundle = await _bundleRepository.LoadFromFileAsync(path);
        // opening a .mpvm the user has not opened before adds it to history; repeats are a no-op
        Remember(path);
        _historyService.SetLastDatabasePath(path);
    }

    public void NewFile()
    {
        _bundleSession.Bundle = new MediaBundleModel();
        _bundleRepository.ClearSelectedFile();
    }

    private void Remember(string path) =>
        _historyService.AddEntry(new MediaRecord(Path.GetFileNameWithoutExtension(path), path));
}
