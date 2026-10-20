using mpvmux.Services;
using System;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class MainWindowViewModel : ViewModelBase
{
    private readonly IBundleFileService _bundleFileService;
    private readonly IHistoryService _historyService;
    private readonly IDialogHelper _dialogHelper;

    public BundleSession Session { get; }

    public TopMenuViewModel TopMenuViewModel { get; }
    public BottomPanelViewModel BottomPanelViewModel { get; }

    public MainWindowViewModel(
        IBundleFileService bundleFileService,
        IHistoryService historyService,
        IDialogHelper dialogHelper,
        BundleSession bundleSession,
        TopMenuViewModel topMenuViewModel,
        BottomPanelViewModel bottomPanelViewModel
    )
    {
        _bundleFileService = bundleFileService;
        _historyService = historyService;
        _dialogHelper = dialogHelper;

        Session = bundleSession;

        TopMenuViewModel = topMenuViewModel;
        BottomPanelViewModel = bottomPanelViewModel;
    }

    public async Task InitializeAsync()
    {
        string lastLoadedSession = _historyService.GetLastDatabasePath();
        if (string.IsNullOrEmpty(lastLoadedSession))
            return;

        try
        {
            await _bundleFileService.LoadAsync(lastLoadedSession);
            TopMenuViewModel.History.SelectByPath(lastLoadedSession);
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to load the last session '{lastLoadedSession}': {ex.Message}");
        }
    }
}