using Avalonia.Input;
using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace mpvmux.Services;

internal interface IShortcutService
{
    IReadOnlyList<ShortcutAction> Actions { get; }

    /// <summary>True while the system-wide hook is running.</summary>
    bool IsGlobalActive { get; }

    /// <summary>True when the action is handled while mpvmux is not focused.</summary>
    bool IsGlobal(string actionId);

    event EventHandler? Changed;

    /// <summary>Raised on the UI thread when a global shortcut fires.</summary>
    event EventHandler<string>? ShortcutTriggered;

    /// <summary>Raised on the UI thread when the global hook fails asynchronously.</summary>
    event EventHandler<string>? GlobalShortcutFailed;

    KeyGesture GetGesture(string actionId);

    /// <summary>
    /// Replaces the override map with <paramref name="gestures"/> (falling back to the current
    /// value for actions not present), stores the actions listed in
    /// <paramref name="globalActionIds"/> as the global ones, validates everything, persists it,
    /// and raises <see cref="Changed"/>.
    /// </summary>
    bool Apply(
        IReadOnlyDictionary<string, KeyGesture> gestures,
        IReadOnlyCollection<string> globalActionIds,
        out string? error);

    /// <summary>
    /// Starts the global hook with the gestures of the global actions, or stops it when none are
    /// global. Call after changing the selection or the gestures.
    /// </summary>
    bool RefreshGlobalMode(out string? error);

    /// <summary>Stops the hook while a modal dialog is open so captured keys do not fire actions.</summary>
    void SuspendGlobalShortcuts();

    void ResumeGlobalShortcuts();
}

internal sealed class ShortcutService : IShortcutService
{
    private static readonly Dictionary<string, KeyGesture> Defaults =
        ShortcutActions.All.ToDictionary(action => action.Id, action => KeyGesture.Parse(action.DefaultGesture));

    private static readonly KeyGesture ExitGesture = KeyGesture.Parse("Alt+F4");

    private readonly IConfigService _configService;
    private readonly IGlobalShortcutHook _hook;
    private bool _suspended;

    public ShortcutService(IConfigService configService, IGlobalShortcutHook hook)
    {
        _configService = configService;
        _hook = hook;

        _hook.Triggered += (_, actionId) => ShortcutTriggered?.Invoke(this, actionId);
        _hook.Failed += (_, message) =>
        {
            GlobalShortcutFailed?.Invoke(this, message);

            // the hook is gone, so the window must take the shortcuts over again
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public IReadOnlyList<ShortcutAction> Actions => ShortcutActions.All;

    public bool IsGlobalActive => _hook.IsRunning;

    public bool IsGlobal(string actionId) =>
        _configService.Config.GlobalShortcutActions.Contains(actionId);

    public event EventHandler? Changed;
    public event EventHandler<string>? ShortcutTriggered;
    public event EventHandler<string>? GlobalShortcutFailed;

    public KeyGesture GetGesture(string actionId)
    {
        // a hand-edited value that is unparseable or reserved must not disable typing in the app
        if (_configService.Config.ShortcutOverrides.TryGetValue(actionId, out var text) &&
            TryParse(text, out var gesture) &&
            IsAllowed(gesture, out _))
        {
            return gesture;
        }

        return Defaults[actionId];
    }

    public bool Apply(
        IReadOnlyDictionary<string, KeyGesture> gestures,
        IReadOnlyCollection<string> globalActionIds,
        out string? error)
    {
        error = null;

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>();

        foreach (var action in ShortcutActions.All)
        {
            var gesture = gestures.TryGetValue(action.Id, out var pending) ? pending : GetGesture(action.Id);

            if (!IsAllowed(gesture, out var reason))
            {
                error = $"{action.Label}: {reason}.";
                return false;
            }

            var text = gesture.ToString();
            if (seen.TryGetValue(text, out var owner))
            {
                var ownerLabel = ShortcutActions.All.First(a => a.Id == owner).Label;
                error = $"{text} is already assigned to {ownerLabel}.";
                return false;
            }

            seen[text] = action.Id;

            if (!gesture.Equals(Defaults[action.Id]))
                overrides[action.Id] = text;
        }

        // only ids that belong to a known action are persisted, in the list's display order
        var globals = ShortcutActions.All
            .Where(action => globalActionIds.Contains(action.Id))
            .Select(action => action.Id)
            .ToList();

        // assigning new collections makes ConfigService mark itself dirty
        _configService.Config.ShortcutOverrides = overrides;
        _configService.Config.GlobalShortcutActions = globals;
        _configService.Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RefreshGlobalMode(out string? error)
    {
        error = null;

        var globals = ShortcutActions.All.Where(action => IsGlobal(action.Id)).ToList();

        if (_suspended || globals.Count == 0)
        {
            _hook.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        var gestures = globals.ToDictionary(action => action.Id, action => GetGesture(action.Id));
        var started = _hook.TryStart(gestures, out error);

        Changed?.Invoke(this, EventArgs.Empty);
        return started;
    }

    public void SuspendGlobalShortcuts()
    {
        _suspended = true;
        _hook.Stop();
    }

    public void ResumeGlobalShortcuts()
    {
        _suspended = false;
        RefreshGlobalMode(out _);
    }

    public static bool IsAllowed(KeyGesture gesture, out string? reason)
    {
        reason = null;

        if (gesture.Equals(ExitGesture))
        {
            reason = "Alt+F4 is reserved for closing the window";
            return false;
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            gesture.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            IsFunctionKey(gesture.Key))
        {
            return true;
        }

        reason = "Use Ctrl or Alt as a modifier, or a function key (F1–F24)";
        return false;
    }

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static bool TryParse(string text, out KeyGesture gesture)
    {
        try
        {
            gesture = KeyGesture.Parse(text);
            return true;
        }
        catch (Exception)
        {
            // a hand-edited or stale value must not prevent startup
            gesture = null!;
            return false;
        }
    }
}
