using mpvmux.Models;
using mpvmux.Services;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class MainWindowViewModel : ViewModelBase
{
    private readonly IVideoRepositoryService _videoRepositoryService;
    private readonly IHistoryService _historyService;

    public MediaContext MediaContext { get; }

    public TopMenuViewModel TopMenuViewModel { get; }
    public BottomPanelViewModel BottomPanelViewModel { get; }

    public MainWindowViewModel(
        IVideoRepositoryService videoRepositoryService,
        IHistoryService historyService,
        MediaContext mediaContext,
        TopMenuViewModel topMenuViewModel,
        BottomPanelViewModel bottomPanelViewModel
    )
    {
        _videoRepositoryService = videoRepositoryService;
        _historyService = historyService;

        MediaContext = mediaContext;

        TopMenuViewModel = topMenuViewModel;
        BottomPanelViewModel = bottomPanelViewModel;

        _videoRepositoryService.OnBundleChanged += OnBundleChanged;
    }


    private async void OnBundleChanged(object? sender, AddedVideoEventArgs e)
    {
        MediaContext.Bundle = MediaBundleModel.FromDto(_videoRepositoryService.Bundle);
        await _historyService.AddEntryAsync(new MediaRecord(e.Key, e.Value));
    }

    public async Task InitializeAsync()
    {
        string lastLoadedSession = _historyService.GetLastDatabasePath();
        if (!string.IsNullOrEmpty(lastLoadedSession))
        {
            await _videoRepositoryService.LoadFromFileAsync(lastLoadedSession);

            TopMenuViewModel.UpdateSelectedIndexByPath(lastLoadedSession);
        }
    }
}