using Avalonia.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        ReportSettingsRecovery();

        await LoadLastSessionAsync();
        await EnsureMpvConfiguredAsync();
        await TopMenuViewModel.CheckForUpdatesOnStartupAsync();

        if (!_shortcutService.RefreshGlobalMode(out var shortcutError) && shortcutError is not null)
            _dialogHelper.ShowError(shortcutError);
    }

    /// <summary>
    /// config.json and history.json are read while the services are constructed, before a window
    /// exists. The reports are collected here so the user learns once — after the window is up —
    /// that a damaged file was set aside and whether their previous content could be restored.
    /// </summary>
    private void ReportSettingsRecovery()
    {
        var reports = new[] { _configService.TakeRecoveryReport(), _historyService.TakeRecoveryReport() }
            .Where(report => report is { NeedsUserAttention: true })
            .Select(report => report!)
            .ToList();

        if (reports.Count == 0)
            return;

        var lines = new List<string>
        {
            $"Could not read {string.Join(" and ", reports.Select(report => report.FileName))}."
        };

        var quarantineFolder = reports
            .Where(report => !string.IsNullOrEmpty(report.QuarantinedPath))
            .Select(report => Path.GetDirectoryName(report.QuarantinedPath!))
            .FirstOrDefault();

        var subject = reports.Count == 1 ? "file" : "files";

        lines.Add(quarantineFolder is not null
            ? $"The damaged {subject} {(reports.Count == 1 ? "was" : "were")} moved to '{quarantineFolder}'."
            : $"The damaged {subject} could not be moved aside.");

        foreach (var report in reports)
        {
            lines.Add(report.Outcome == SettingsLoadOutcome.RecoveredFromBackup
                ? $"{report.FileName} was restored from its last good copy."
                : $"{report.FileName}: defaults are in use.");
        }

        _dialogHelper.ShowInfo(string.Join(" ", lines));
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

        // the history view owns the load lock and the backup-restore prompt
        var opened = await TopMenuViewModel.History.OpenAsync(lastLoadedSession);
        if (opened)
            return;

        // do not try (and fail) on the same corrupt file every launch
        _historyService.SetLastDatabasePath("");
        _historyService.SaveHistory();
    }
}