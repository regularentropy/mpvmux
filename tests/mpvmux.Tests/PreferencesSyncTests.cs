using Avalonia.Input;
using mpvmux.Models;
using mpvmux.Services;
using mpvmux.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace mpvmux.Tests;

public sealed class PreferencesSyncTests
{
    private sealed class FakeConfigService(ConfigModel config) : IConfigService
    {
        public ConfigModel Config { get; } = config;
        public bool IsDirty => true;
        public void Save() { }
    }

    private sealed class StubShortcutService : IShortcutService
    {
        public IReadOnlyList<ShortcutAction> Actions => ShortcutActions.All;
        public bool IsGlobalActive => false;
        public event EventHandler? Changed { add { } remove { } }
        public event EventHandler<string>? ShortcutTriggered { add { } remove { } }
        public event EventHandler<string>? GlobalShortcutFailed { add { } remove { } }
        public KeyGesture GetGesture(string actionId) =>
            KeyGesture.Parse(Actions.First(action => action.Id == actionId).DefaultGesture);
        public bool IsGlobal(string actionId) => false;
        public bool Apply(
            IReadOnlyDictionary<string, KeyGesture> gestures,
            IReadOnlyCollection<string> globalActionIds,
            out string? error)
        {
            error = null;
            return true;
        }
        public bool RefreshGlobalMode(out string? error)
        {
            error = null;
            return true;
        }
        public void SuspendGlobalShortcuts() { }
        public void ResumeGlobalShortcuts() { }
    }

    private static PreferencesWindowViewModel NewViewModel(ConfigModel config) =>
        // only the shortcut service is used during construction; the picker and dialogs are
        // irrelevant for config sync
        new(new FakeConfigService(config), null!, null!, new StubShortcutService(), null!);

    [Fact]
    public void Changes_made_elsewhere_are_reflected_while_the_dialog_is_open()
    {
        var config = new ConfigModel();
        var viewModel = NewViewModel(config);

        config.EnableAutomaticPlayback = true;
        config.EnableFullscreen = true;

        Assert.True(viewModel.EnableAutomaticPlayback);
        Assert.True(viewModel.EnableFullscreen);
    }

    [Fact]
    public void Detach_stops_tracking_config_changes()
    {
        var config = new ConfigModel();
        var viewModel = NewViewModel(config);
        viewModel.Detach();

        config.EnableAutomaticPlayback = true;

        Assert.False(viewModel.EnableAutomaticPlayback);
    }
}
