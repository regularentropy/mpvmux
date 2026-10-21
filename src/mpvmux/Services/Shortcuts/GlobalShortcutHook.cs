using Avalonia.Input;
using Avalonia.Threading;
using mpvmux.Models;
using SharpHook;
using SharpHook.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IGlobalShortcutHook
{
    bool IsRunning { get; }

    event EventHandler<string>? Triggered;
    event EventHandler<string>? Failed;

    bool TryStart(IReadOnlyDictionary<string, KeyGesture> gestures, out string? error);
    void Stop();
}

/// <summary>
/// System-wide keyboard hook backed by SharpHook. It filters the global key stream down to the
/// configured combinations; on Windows and macOS matching events are also swallowed so the
/// focused application does not receive them.
/// </summary>
internal sealed class GlobalShortcutHook : IGlobalShortcutHook, IDisposable
{
    // suppression is a SharpHook feature only available on these platforms
    private static readonly bool CanSuppress = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private readonly object _gate = new();
    private readonly HashSet<KeyCode> _heldKeys = [];
    private Dictionary<(KeyCode Key, EventMask Modifiers), string> _bindings = [];
    private SimpleGlobalHook? _hook;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _hook is not null;
        }
    }

    public event EventHandler<string>? Triggered;
    public event EventHandler<string>? Failed;

    public bool TryStart(IReadOnlyDictionary<string, KeyGesture> gestures, out string? error)
    {
        lock (_gate)
        {
            StopCore();

            if (!TryBuildBindings(gestures, out var bindings, out error))
                return false;

            _bindings = bindings;

            var hook = new SimpleGlobalHook();
            hook.KeyPressed += OnKeyPressed;
            hook.KeyReleased += OnKeyReleased;
            _hook = hook;

            try
            {
                _ = hook.RunAsync(GlobalHookType.Keyboard)
                    .ContinueWith(task => OnHookExited(hook, task), TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _hook = null;
                _bindings = [];
                hook.Dispose();
                error = $"Global shortcuts could not start: {ex.Message}";
                return false;
            }

            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopCore();
    }

    private void StopCore()
    {
        var hook = _hook;
        _hook = null;
        _bindings = [];
        _heldKeys.Clear();

        if (hook is null)
            return;

        try
        {
            hook.Stop();
        }
        catch (Exception)
        {
            // already stopped or disposed
        }

        try
        {
            hook.Dispose();
        }
        catch (Exception)
        {
            // nothing useful to do during shutdown
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = e.Data.KeyCode;

        if (IsModifierKey(key))
            return;

        var modifiers = NormalizeModifiers(e.RawEvent.Mask);
        string? actionId;

        lock (_gate)
        {
            if (!_bindings.TryGetValue((key, modifiers), out actionId))
                return;

            // auto-repeat while the combination is held must not fire the action repeatedly
            if (!_heldKeys.Add(key))
                actionId = null;

            if (CanSuppress)
                e.SuppressEvent = true;
        }

        if (actionId is not null)
            Dispatcher.UIThread.Post(() => Triggered?.Invoke(this, actionId));
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        lock (_gate)
            _heldKeys.Remove(e.Data.KeyCode);
    }

    private void OnHookExited(SimpleGlobalHook hook, Task task)
    {
        if (!task.IsFaulted)
            return;

        var message = task.Exception?.GetBaseException().Message ?? "unknown error";

        lock (_gate)
        {
            if (ReferenceEquals(_hook, hook))
            {
                _hook = null;
                _bindings = [];
                _heldKeys.Clear();
            }
        }

        try
        {
            hook.Dispose();
        }
        catch (Exception)
        {
            // already disposed
        }

        Dispatcher.UIThread.Post(() => Failed?.Invoke(this, $"Global shortcuts stopped: {message}"));
    }

    private static bool TryBuildBindings(
        IReadOnlyDictionary<string, KeyGesture> gestures,
        out Dictionary<(KeyCode Key, EventMask Modifiers), string> bindings,
        out string? error)
    {
        bindings = [];
        error = null;

        foreach (var (actionId, gesture) in gestures)
        {
            if (ToKeyCode(gesture.Key) is not { } key)
            {
                var label = ShortcutActions.All.FirstOrDefault(action => action.Id == actionId)?.Label ?? actionId;
                error = $"{label}: this key cannot be used for global shortcuts.";
                return false;
            }

            var combo = (key, ToEventMask(gesture.KeyModifiers));

            if (!bindings.TryAdd(combo, actionId))
            {
                error = "Two shortcuts use the same key combination.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collapses libuiohook's side-specific modifier bits (left/right Ctrl, Shift, Alt and Meta) to
    /// the generic flags used by <see cref="ToEventMask"/>. A raw mask contains only the bit of the
    /// side that is actually pressed, so an exact comparison against e.g. <see cref="EventMask.Ctrl"/>
    /// (left and right combined) would never match.
    /// </summary>
    private static EventMask NormalizeModifiers(EventMask mask)
    {
        var modifiers = EventMask.None;

        if (mask.HasCtrl())
            modifiers |= EventMask.Ctrl;
        if (mask.HasShift())
            modifiers |= EventMask.Shift;
        if (mask.HasAlt())
            modifiers |= EventMask.Alt;
        if (mask.HasMeta())
            modifiers |= EventMask.Meta;

        return modifiers;
    }

    private static EventMask ToEventMask(KeyModifiers modifiers)
    {
        var mask = EventMask.None;

        if (modifiers.HasFlag(KeyModifiers.Control))
            mask |= EventMask.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            mask |= EventMask.Shift;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            mask |= EventMask.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            mask |= EventMask.Meta;

        return mask;
    }

    private static bool IsModifierKey(KeyCode key) => key is
        KeyCode.VcLeftShift or KeyCode.VcRightShift or
        KeyCode.VcLeftControl or KeyCode.VcRightControl or
        KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftMeta or KeyCode.VcRightMeta or
        KeyCode.VcCapsLock or KeyCode.VcNumLock or KeyCode.VcScrollLock;

    private static KeyCode? ToKeyCode(Key key) => key switch
    {
        _ when key is >= Key.A and <= Key.Z => (KeyCode)((int)KeyCode.VcA + (key - Key.A)),
        _ when key is >= Key.F1 and <= Key.F24 => (KeyCode)((int)KeyCode.VcF1 + (key - Key.F1)),

        // digits and numpad keys are not contiguous in libuiohook (0 comes after 9)
        Key.D0 => KeyCode.Vc0,
        Key.D1 => KeyCode.Vc1,
        Key.D2 => KeyCode.Vc2,
        Key.D3 => KeyCode.Vc3,
        Key.D4 => KeyCode.Vc4,
        Key.D5 => KeyCode.Vc5,
        Key.D6 => KeyCode.Vc6,
        Key.D7 => KeyCode.Vc7,
        Key.D8 => KeyCode.Vc8,
        Key.D9 => KeyCode.Vc9,
        Key.NumPad0 => KeyCode.VcNumPad0,
        Key.NumPad1 => KeyCode.VcNumPad1,
        Key.NumPad2 => KeyCode.VcNumPad2,
        Key.NumPad3 => KeyCode.VcNumPad3,
        Key.NumPad4 => KeyCode.VcNumPad4,
        Key.NumPad5 => KeyCode.VcNumPad5,
        Key.NumPad6 => KeyCode.VcNumPad6,
        Key.NumPad7 => KeyCode.VcNumPad7,
        Key.NumPad8 => KeyCode.VcNumPad8,
        Key.NumPad9 => KeyCode.VcNumPad9,

        Key.Return => KeyCode.VcEnter,
        Key.Escape => KeyCode.VcEscape,
        Key.Back => KeyCode.VcBackspace,
        Key.Tab => KeyCode.VcTab,
        Key.Space => KeyCode.VcSpace,
        Key.Insert => KeyCode.VcInsert,
        Key.Delete => KeyCode.VcDelete,
        Key.Home => KeyCode.VcHome,
        Key.End => KeyCode.VcEnd,
        Key.PageUp => KeyCode.VcPageUp,
        Key.PageDown => KeyCode.VcPageDown,
        Key.Left => KeyCode.VcLeft,
        Key.Right => KeyCode.VcRight,
        Key.Up => KeyCode.VcUp,
        Key.Down => KeyCode.VcDown,

        Key.Multiply => KeyCode.VcNumPadMultiply,
        Key.Add => KeyCode.VcNumPadAdd,
        Key.Subtract => KeyCode.VcNumPadSubtract,
        Key.Decimal => KeyCode.VcNumPadDecimal,
        Key.Divide => KeyCode.VcNumPadDivide,

        Key.OemSemicolon => KeyCode.VcSemicolon,
        Key.OemPlus => KeyCode.VcEquals,
        Key.OemComma => KeyCode.VcComma,
        Key.OemMinus => KeyCode.VcMinus,
        Key.OemPeriod => KeyCode.VcPeriod,
        Key.OemQuestion => KeyCode.VcSlash,
        Key.OemTilde => KeyCode.VcBackQuote,
        Key.OemOpenBrackets => KeyCode.VcOpenBracket,
        Key.OemCloseBrackets => KeyCode.VcCloseBracket,
        Key.OemPipe => KeyCode.VcBackslash,
        Key.OemBackslash => KeyCode.VcBackslash,
        Key.OemQuotes => KeyCode.VcQuote,

        _ => null,
    };

    public void Dispose() => Stop();
}
