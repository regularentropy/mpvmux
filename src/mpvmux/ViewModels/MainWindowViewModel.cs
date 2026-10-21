using Avalonia.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace mpvmux.ViewModels;

internal partial class MainWindowViewModel : ViewModelBase
{
    private readonly IBundleFileService _bundleFileService;
    private readonly IHistoryService _historyService;
    private readonly IDialogHelper _dialogHelper;
    private readonly IShortcutService _shortcutService;

    public BundleSession Session { get; }

    public TopMenuViewModel TopMenuViewModel { get; }
    public BottomPanelViewModel BottomPanelViewModel { get; }
    public TrackTableViewModel TrackTableViewModel { get; }
    public ShortcutGesturesViewModel Shortcuts { get; }

    public event EventHandler? ShortcutsChanged;

    public MainWindowViewModel(
        IBundleFileService bundleFileService,
        IHistoryService historyService,
        IDialogHelper dialogHelper,
        BundleSession bundleSession,
        TopMenuViewModel topMenuViewModel,
        BottomPanelViewModel bottomPanelViewModel,
        TrackTableViewModel trackTableViewModel,
        ShortcutGesturesViewModel shortcuts,
        IShortcutService shortcutService
    )
    {
        _bundleFileService = bundleFileService;
        _historyService = historyService;
        _dialogHelper = dialogHelper;
        _shortcutService = shortcutService;

        Session = bundleSession;

        TopMenuViewModel = topMenuViewModel;
        BottomPanelViewModel = bottomPanelViewModel;
        TrackTableViewModel = trackTableViewModel;
        Shortcuts = shortcuts;

        shortcutService.Changed += (_, _) => ShortcutsChanged?.Invoke(this, EventArgs.Empty);
        shortcutService.ShortcutTriggered += OnShortcutTriggered;
        shortcutService.GlobalShortcutFailed += (_, message) => _dialogHelper.ShowError(message);
    }

    /// <summary>
    /// The window rebuilds its <see cref="KeyBinding"/> list from this whenever a gesture or the
    /// global mode changes. While the system-wide hook is active it owns the shortcuts, so the
    /// window must not handle the same keys a second time.
    /// </summary>
    public IReadOnlyList<(KeyGesture Gesture, ICommand Command)> CreateShortcutBindings()
    {
        if (_shortcutService.IsGlobalActive)
            return [];

        return
        [
            (Shortcuts.NewBundle, TopMenuViewModel.FileMenu.NewFileCommand),
            (Shortcuts.OpenBundle, TopMenuViewModel.FileMenu.LoadFileCommand),
            (Shortcuts.SaveBundle, TopMenuViewModel.FileMenu.SaveCommand),
            (Shortcuts.SaveBundleAs, TopMenuViewModel.FileMenu.SaveAsCommand),
            (Shortcuts.Play, BottomPanelViewModel.LaunchMPVCommand),
            (Shortcuts.Pause, BottomPanelViewModel.PauseCommand),
            (Shortcuts.Stop, BottomPanelViewModel.StopCommand),
            (Shortcuts.PreviousTrack, BottomPanelViewModel.PreviousTrackCommand),
            (Shortcuts.NextTrack, BottomPanelViewModel.NextTrackCommand),
            (Shortcuts.ToggleAutoplay, TopMenuViewModel.ToggleAutoplayCommand),
            (Shortcuts.ToggleFullscreen, TopMenuViewModel.ToggleFullscreenCommand),
            (Shortcuts.CheckForUpdates, TopMenuViewModel.CheckForUpdatesCommand),
            (Shortcuts.About, TopMenuViewModel.ShowAboutMenuCommand),
        ];
    }

    private void OnShortcutTriggered(object? sender, string actionId)
    {
        ICommand? command = actionId switch
        {
            ShortcutActions.NewBundle => TopMenuViewModel.FileMenu.NewFileCommand,
            ShortcutActions.OpenBundle => TopMenuViewModel.FileMenu.LoadFileCommand,
            ShortcutActions.SaveBundle => TopMenuViewModel.FileMenu.SaveCommand,
            ShortcutActions.SaveBundleAs => TopMenuViewModel.FileMenu.SaveAsCommand,
            ShortcutActions.Play => BottomPanelViewModel.LaunchMPVCommand,
            ShortcutActions.Pause => BottomPanelViewModel.PauseCommand,
            ShortcutActions.Stop => BottomPanelViewModel.StopCommand,
            ShortcutActions.PreviousTrack => BottomPanelViewModel.PreviousTrackCommand,
            ShortcutActions.NextTrack => BottomPanelViewModel.NextTrackCommand,
            ShortcutActions.ToggleAutoplay => TopMenuViewModel.ToggleAutoplayCommand,
            ShortcutActions.ToggleFullscreen => TopMenuViewModel.ToggleFullscreenCommand,
            ShortcutActions.CheckForUpdates => TopMenuViewModel.CheckForUpdatesCommand,
            ShortcutActions.About => TopMenuViewModel.ShowAboutMenuCommand,
            _ => null,
        };

        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    public async Task InitializeAsync()
    {
        await LoadLastSessionAsync();
        await TopMenuViewModel.CheckForUpdatesOnStartupAsync();

        if (!_shortcutService.RefreshGlobalMode(out var shortcutError) && shortcutError is not null)
            _dialogHelper.ShowError(shortcutError);
    }

    private async Task LoadLastSessionAsync()
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