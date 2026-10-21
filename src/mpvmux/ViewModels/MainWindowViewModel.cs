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
    private readonly IConfigService _configService;
    private readonly IMpvLocator _mpvLocator;
    private readonly IWindowService _windowService;

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
        IShortcutService shortcutService,
        IConfigService configService,
        IMpvLocator mpvLocator,
        IWindowService windowService
    )
    {
        _bundleFileService = bundleFileService;
        _historyService = historyService;
        _dialogHelper = dialogHelper;
        _shortcutService = shortcutService;
        _configService = configService;
        _mpvLocator = mpvLocator;
        _windowService = windowService;

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
    /// scope of an action changes. Global actions are left out because the system-wide hook owns
    /// them; handling them here as well would fire the command twice.
    /// </summary>
    public IReadOnlyList<(KeyGesture Gesture, ICommand Command)> CreateShortcutBindings()
    {
        var bindings = new List<(KeyGesture, ICommand)>();
        Add(ShortcutActions.NewBundle, Shortcuts.NewBundle, TopMenuViewModel.FileMenu.NewFileCommand);
        Add(ShortcutActions.OpenBundle, Shortcuts.OpenBundle, TopMenuViewModel.FileMenu.LoadFileCommand);
        Add(ShortcutActions.SaveBundle, Shortcuts.SaveBundle, TopMenuViewModel.FileMenu.SaveCommand);
        Add(ShortcutActions.SaveBundleAs, Shortcuts.SaveBundleAs, TopMenuViewModel.FileMenu.SaveAsCommand);
        Add(ShortcutActions.Play, Shortcuts.Play, BottomPanelViewModel.LaunchMPVCommand);
        Add(ShortcutActions.Pause, Shortcuts.Pause, BottomPanelViewModel.PauseCommand);
        Add(ShortcutActions.Stop, Shortcuts.Stop, BottomPanelViewModel.StopCommand);
        Add(ShortcutActions.PreviousTrack, Shortcuts.PreviousTrack, BottomPanelViewModel.PreviousTrackCommand);
        Add(ShortcutActions.NextTrack, Shortcuts.NextTrack, BottomPanelViewModel.NextTrackCommand);
        Add(ShortcutActions.ToggleAutoplay, Shortcuts.ToggleAutoplay, TopMenuViewModel.ToggleAutoplayCommand);
        Add(ShortcutActions.ToggleFullscreen, Shortcuts.ToggleFullscreen, TopMenuViewModel.ToggleFullscreenCommand);
        Add(ShortcutActions.CheckForUpdates, Shortcuts.CheckForUpdates, TopMenuViewModel.CheckForUpdatesCommand);
        Add(ShortcutActions.About, Shortcuts.About, TopMenuViewModel.ShowAboutMenuCommand);

        return bindings;

        void Add(string actionId, KeyGesture gesture, ICommand command)
        {
            if (!_shortcutService.IsGlobal(actionId))
                bindings.Add((gesture, command));
        }
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
        await EnsureMpvConfiguredAsync();
        await TopMenuViewModel.CheckForUpdatesOnStartupAsync();

        if (!_shortcutService.RefreshGlobalMode(out var shortcutError) && shortcutError is not null)
            _dialogHelper.ShowError(shortcutError);
    }

    /// <summary>
    /// Until the mpv setup has been completed, try to find a usable player. A successful
    /// detection is saved silently; otherwise the setup window is offered once.
    /// </summary>
    private async Task EnsureMpvConfiguredAsync()
    {
        if (_configService.Config.MpvSetupCompleted)
            return;

        var result = await _mpvLocator.ResolveAsync(_configService.Config.MpvPath);
        if (!result.IsFound)
            result = await _mpvLocator.LocateAsync();

        if (result.IsFound)
        {
            _configService.Config.MpvPath = result.ExecutablePath!;
            _configService.Config.MpvSetupCompleted = true;
            _configService.Save();
            return;
        }

        await _windowService.ShowSetupAsync();
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