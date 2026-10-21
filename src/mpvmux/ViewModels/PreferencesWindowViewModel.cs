using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class PreferencesWindowViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogHelper _dialogHelper;
    private readonly IShortcutService _shortcutService;
    private readonly IMpvLocator _mpvLocator;

    public PreferencesWindowViewModel(
        IConfigService configService,
        IFilePickerService filePickerService,
        IDialogHelper dialogHelper,
        IShortcutService shortcutService,
        IMpvLocator mpvLocator)
    {
        _configService = configService;
        _filePickerService = filePickerService;
        _dialogHelper = dialogHelper;
        _shortcutService = shortcutService;
        _mpvLocator = mpvLocator;

        _mpvPath = configService.Config.MpvPath;
        _enableAutomaticPlayback = configService.Config.EnableAutomaticPlayback;
        _enableFullscreen = configService.Config.EnableFullscreen;
        _checkForUpdatesOnStartup = configService.Config.CheckForUpdatesOnStartup;
        _enableGlobalShortcuts = configService.Config.EnableGlobalShortcuts;
        _recentLimit = configService.Config.RecentLimit;

        Shortcuts = new(shortcutService.Actions.Select(action =>
            new ShortcutEntryViewModel(action, shortcutService.GetGesture(action.Id))));

        shortcutService.Changed += OnShortcutsChanged;
        shortcutService.ShortcutTriggered += OnShortcutTriggered;
        shortcutService.GlobalShortcutFailed += OnGlobalShortcutFailed;
        UpdateGlobalStatus();
    }

    [ObservableProperty]
    private string _mpvPath;

    [ObservableProperty]
    private bool _enableAutomaticPlayback;

    [ObservableProperty]
    private bool _enableFullscreen;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup;

    [ObservableProperty]
    private bool _enableGlobalShortcuts;

    [ObservableProperty]
    private int _recentLimit;

    [ObservableProperty]
    private string _globalShortcutStatus = "";

    [ObservableProperty]
    private string _lastGlobalShortcut = "";

    public ObservableCollection<ShortcutEntryViewModel> Shortcuts { get; }

    partial void OnEnableGlobalShortcutsChanged(bool value) => UpdateGlobalStatus();

    [RelayCommand]
    private async Task Browse()
    {
        var path = await _filePickerService.PickExecutableAsync();
        if (path is not null)
            MpvPath = path;
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var path = await _filePickerService.PickFolderAsync();
        if (path is not null)
            MpvPath = path;
    }

    [RelayCommand]
    private void ResetShortcuts()
    {
        foreach (var shortcut in Shortcuts)
            shortcut.Reset();
    }

    /// <summary>
    /// Applies a captured gesture to <paramref name="entry"/>, or returns a message explaining
    /// why it was rejected (bad modifier or a clash with another pending shortcut).
    /// </summary>
    public string? TrySetShortcut(ShortcutEntryViewModel entry, KeyGesture gesture)
    {
        if (!ShortcutService.IsAllowed(gesture, out var reason))
            return $"{entry.Label}: {reason}.";

        var text = gesture.ToString();
        var owner = Shortcuts.FirstOrDefault(other =>
            other.Id != entry.Id && string.Equals(other.GestureText, text, StringComparison.OrdinalIgnoreCase));

        if (owner is not null)
            return $"{text} is already assigned to {owner.Label}.";

        entry.Gesture = gesture;
        return null;
    }

    public async Task<bool> TryApplyAsync()
    {
        if (string.IsNullOrWhiteSpace(MpvPath))
        {
            _dialogHelper.ShowError("MPV path cannot be empty.");
            return false;
        }

        // accepts an executable, a folder containing mpv, or a bare command from PATH
        var mpv = await _mpvLocator.ResolveAsync(MpvPath);
        if (!mpv.IsFound)
        {
            _dialogHelper.ShowError(mpv.Status switch
            {
                MpvProbeStatus.NotExecutable => $"MPV could not be started: {MpvPath}",
                MpvProbeStatus.NotMpv => $"That file does not look like mpv: {MpvPath}",
                _ => $"MPV executable not found: {MpvPath}",
            });
            return false;
        }

        MpvPath = mpv.ExecutablePath!;

        // validate and persist the shortcuts first: a rejected gesture must not leave the other
        // settings half-applied
        var gestures = Shortcuts.ToDictionary(shortcut => shortcut.Id, shortcut => shortcut.Gesture);
        if (!_shortcutService.Apply(gestures, out var shortcutError))
        {
            _dialogHelper.ShowError(shortcutError ?? "One of the shortcuts is invalid.");
            return false;
        }

        _configService.Config.MpvPath = MpvPath;
        _configService.Config.MpvSetupCompleted = true;
        _configService.Config.EnableAutomaticPlayback = EnableAutomaticPlayback;
        _configService.Config.EnableFullscreen = EnableFullscreen;
        _configService.Config.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
        _configService.Config.EnableGlobalShortcuts = EnableGlobalShortcuts;
        RecentLimit = Math.Clamp(RecentLimit, ConfigModel.MinRecentLimit, ConfigModel.MaxRecentLimit);
        _configService.Config.RecentLimit = RecentLimit;
        _configService.Save();

        if (!_shortcutService.RefreshGlobalMode(out var globalError))
        {
            // do not leave the app configured to retry a setup that cannot work
            _configService.Config.EnableGlobalShortcuts = false;
            _configService.Save();
            EnableGlobalShortcuts = false;
            _dialogHelper.ShowError(globalError ?? "Global shortcuts could not be started.");
            return false;
        }

        UpdateGlobalStatus();
        return true;
    }

    /// <summary>
    /// The hook would otherwise fire an action for the combination the user is trying to capture.
    /// </summary>
    public void BeginShortcutCapture() => _shortcutService.SuspendGlobalShortcuts();

    public void EndShortcutCapture()
    {
        _shortcutService.ResumeGlobalShortcuts();
        UpdateGlobalStatus();
    }

    /// <summary>Releases the service subscriptions when the dialog closes.</summary>
    public void Detach()
    {
        _shortcutService.Changed -= OnShortcutsChanged;
        _shortcutService.ShortcutTriggered -= OnShortcutTriggered;
        _shortcutService.GlobalShortcutFailed -= OnGlobalShortcutFailed;
    }

    private void OnShortcutsChanged(object? sender, EventArgs e) => UpdateGlobalStatus();

    private void OnShortcutTriggered(object? sender, string actionId)
    {
        var label = ShortcutActions.All.FirstOrDefault(action => action.Id == actionId)?.Label ?? actionId;
        LastGlobalShortcut = $"Last global shortcut: {label}.";
        UpdateGlobalStatus();
    }

    private void OnGlobalShortcutFailed(object? sender, string message)
    {
        LastGlobalShortcut = message;
        UpdateGlobalStatus();
    }

    private void UpdateGlobalStatus()
    {
        var saved = _configService.Config.EnableGlobalShortcuts;

        // the dialog is modal but the hook keeps running, so unsaved edits need a clear state
        if (saved != EnableGlobalShortcuts)
        {
            GlobalShortcutStatus = EnableGlobalShortcuts
                ? "Global shortcuts will be enabled when you click Save."
                : "Global shortcuts will be turned off when you click Save.";
            return;
        }

        if (!saved)
        {
            GlobalShortcutStatus = "Global shortcuts are off.";
            return;
        }

        GlobalShortcutStatus = _shortcutService.IsGlobalActive
            ? "Global shortcuts are active. Press one to test it."
            : "Global shortcuts are not running.";
    }
}
