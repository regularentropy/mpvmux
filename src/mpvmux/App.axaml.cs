using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.Constants;
using mpvmux.Models;
using mpvmux.Services;
using mpvmux.ViewModels;
using mpvmux.Views;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux;

public partial class App : Application
{
    private static readonly Uri LightTrayIconUri = new("avares://mpvmux/Assets/taskbar-light.ico");
    private static readonly Uri DarkTrayIconUri = new("avares://mpvmux/Assets/taskbar-dark.ico");

    private IConfigService _configService;
    private IHistoryService _historyService;
    private IUnsavedChangesGuard _unsavedChangesGuard;
    private MainWindowViewModel? _viewModel;
    private IDialogHelper? _dialogHelper;
    private InstanceChannel? _instanceChannel;
    private readonly CancellationTokenSource _instanceListener = new();

    // the close handler cancels the first attempt to ask about unsaved edits and then closes
    private bool _closeConfirmed;

    /// <summary>
    /// Set by <see cref="Program"/> when another instance owns the data folder: the app then
    /// shows a single "mpvmux is already running." window and exits instead of composing the
    /// services, the main window and the tray.
    /// </summary>
    internal static bool ShowAlreadyRunningMessage { get; set; }
    private NativeMenuItem? _autoplayMenuItem;
    private NativeMenuItem? _fullscreenMenuItem;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Directory.CreateDirectory(AppConstants.BaseFolderPath);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (ShowAlreadyRunningMessage)
            {
                ShowAlreadyRunning(desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var collection = new ServiceCollection();
            collection.AddCommonServices();

            var services = collection.BuildServiceProvider();

            _historyService = services.GetRequiredService<IHistoryService>();
            _configService = services.GetRequiredService<IConfigService>();
            _unsavedChangesGuard = services.GetRequiredService<IUnsavedChangesGuard>();
            _dialogHelper = services.GetRequiredService<IDialogHelper>();

            ThemeManager.Apply(_configService.Config.UseDarkTheme);

            var vm = services.GetRequiredService<MainWindowViewModel>();
            _viewModel = vm;

            var mainWindow = new MainWindow();
            mainWindow.DataContext = vm;

            desktop.MainWindow = mainWindow;

            mainWindow.Loaded += async (_, _) => await vm.InitializeAsync();

            mainWindow.Closing += OnWindowClosing;

            // the close handler resets the flag, so a later close (tray keeps the app alive)
            // asks again instead of relying on the previous confirmation
            mainWindow.Closed += (_, _) =>
            {
                _closeConfirmed = false;
                _instanceListener.Cancel();
                _instanceChannel?.Dispose();
            };

            // A second launch hands its bundle paths here. The listener is created with the
            // window, which is as early as the UI can take over an open.
            _instanceChannel = InstanceChannel.ForCurrentUser();
            _ = _instanceChannel.RunAsync(OnForwardedBundle, _instanceListener.Token);

            CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// A second launch had nothing to forward: say why this window exists and let the default
    /// shutdown mode end the process when it closes. No services, main window or tray icon.
    /// </summary>
    private static void ShowAlreadyRunning(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new MessageBoxWindow("mpvmux", "mpvmux is already running.")
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        desktop.MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Called on the listener's thread. Opening the bundle the way File > Open does keeps the
    /// unsaved-changes guard in the loop, and showing the window first gives its prompt a
    /// visible owner.
    /// </summary>
    private Task OnForwardedBundle(string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowMainWindow();
            _ = OpenForwardedBundleAsync(path);
        });

        return Task.CompletedTask;
    }

    private async Task OpenForwardedBundleAsync(string path)
    {
        try
        {
            if (_viewModel is not null)
                await _viewModel.TopMenuViewModel.History.OpenAsync(path);
        }
        catch (Exception ex)
        {
            _dialogHelper?.ShowError($"Could not open '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>
    /// Closing is two-pass: the first attempt is cancelled so the unsaved-changes guard can run,
    /// and the confirmed pass saves the settings files and closes for real.
    /// </summary>
    private async void OnWindowClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (_closeConfirmed || sender is not Window window)
            return;

        e.Cancel = true;

        try
        {
            if (!await _unsavedChangesGuard.ConfirmProceedAsync("closing mpvmux"))
                return;
        }
        catch (Exception)
        {
            // failing to ask must not close the window over unsaved edits
            return;
        }

        _historyService.SaveHistory();
        _configService.Save();

        _closeConfirmed = true;
        window.Close();
    }

    private void CreateTrayIcon()
    {
        _autoplayMenuItem = new NativeMenuItem
        {
            Header = "Autoplay",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _configService.Config.EnableAutomaticPlayback
        };
        _autoplayMenuItem.Click += (_, _) => ToggleAutoplay();

        _fullscreenMenuItem = new NativeMenuItem
        {
            Header = "Start playback in fullscreen",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _configService.Config.EnableFullscreen
        };
        _fullscreenMenuItem.Click += (_, _) => ToggleFullscreen();

        var showItem = new NativeMenuItem { Header = "Show mpvmux" };
        showItem.Click += (_, _) => ShowMainWindow();

        var quitItem = new NativeMenuItem { Header = "Exit" };
        quitItem.Click += (_, _) => Quit();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(_autoplayMenuItem);
        menu.Add(_fullscreenMenuItem);
        menu.Add(quitItem);

        // preferences can change the settings while this menu is closed
        menu.NeedsUpdate += (_, _) => SyncTrayMenuItems();

        // On Windows the tray menu is a managed flyout built from the NativeMenu's items, and
        // NeedsUpdate is never raised for it. Push every config change into the items directly
        // so the ticks stay in sync with the Playback menu and Preferences.
        _configService.Config.PropertyChanged += OnConfigPropertyChanged;

        _trayIcon = new TrayIcon
        {
            Icon = LoadTrayIcon(IsDarkTaskbar()),
            ToolTipText = "mpvmux",
            Menu = menu
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

        // the tray sits on the OS taskbar, so its icon follows the system theme,
        // not the app's light/dark setting
        if (PlatformSettings is { } platformSettings)
            platformSettings.ColorValuesChanged += OnPlatformColorValuesChanged;
    }

    private bool IsDarkTaskbar()
        => PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    private static WindowIcon LoadTrayIcon(bool useDarkBackground)
    {
        using var stream = AssetLoader.Open(useDarkBackground ? DarkTrayIconUri : LightTrayIconUri);
        return new WindowIcon(stream);
    }

    private void OnPlatformColorValuesChanged(object? sender, PlatformColorValues e)
    {
        if (_trayIcon is null)
            return;

        Dispatcher.UIThread.Post(() =>
            _trayIcon.Icon = LoadTrayIcon(e.ThemeVariant == PlatformThemeVariant.Dark));
    }

    private void ToggleAutoplay()
    {
        _configService.Config.EnableAutomaticPlayback = !_configService.Config.EnableAutomaticPlayback;
        _configService.Save();
    }

    private void ToggleFullscreen()
    {
        _configService.Config.EnableFullscreen = !_configService.Config.EnableFullscreen;
        _configService.Save();
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConfigModel.EnableAutomaticPlayback):
            case nameof(ConfigModel.EnableFullscreen):
                SyncTrayMenuItems();
                break;
        }
    }

    private void SyncTrayMenuItems()
    {
        if (_autoplayMenuItem is not null)
            _autoplayMenuItem.IsChecked = _configService.Config.EnableAutomaticPlayback;

        if (_fullscreenMenuItem is not null)
            _fullscreenMenuItem.IsChecked = _configService.Config.EnableFullscreen;
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.MainWindow is not { } window)
            return;

        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    private void Quit()
    {
        // Closing the main window runs the unsaved-changes guard, which can cancel; the default
        // shutdown mode then exits once the last window is gone. Shutting the lifetime down here
        // would skip the prompt.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            window.Close();
            return;
        }

        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IBundleBackupStore, BundleBackupStore>();
        collection.AddSingleton<IBundleEditTracker, BundleEditTracker>();
        collection.AddSingleton<IBundleRepository, BundleRepository>();
        collection.AddSingleton<IUpdateService, UpdateService>();
        collection.AddSingleton<IBundleFileService, BundleFileService>();
        collection.AddSingleton<IBundleAutoSaveService, BundleAutoSaveService>();
        collection.AddSingleton<IUnsavedChangesGuard, UnsavedChangesGuard>();
        collection.AddSingleton<IPlayerService, PlayerService>();
        collection.AddSingleton<IDialogHelper, DialogHelper>();

        // one long-lived client: sockets are reused instead of a pool per update check
        collection.AddSingleton(new HttpClient());

        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<IFilePickerService, FilePickerService>();
        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IMpvLocator, MpvLocator>();
        collection.AddSingleton<IBundleRelinkService, BundleRelinkService>();
        collection.AddSingleton<IMediaRelinkService, MediaRelinkService>();
        collection.AddSingleton<IHistoryService, HistoryService>();
        collection.AddSingleton<IShortcutService, ShortcutService>();
        collection.AddSingleton<IGlobalShortcutHook, GlobalShortcutHook>();

        collection.AddSingleton<BundleSession>();
        collection.AddSingleton<AppMetadata>();

        collection.AddSingleton<HistoryViewModel>();
        collection.AddSingleton<FileMenuViewModel>();
        collection.AddSingleton<TopMenuViewModel>();
        collection.AddSingleton<ShortcutGesturesViewModel>();
        collection.AddSingleton<BottomPanelViewModel>();
        collection.AddSingleton<TrackTableViewModel>();
        collection.AddSingleton<AboutWindowViewModel>();

        collection.AddTransient<UpdaterViewModel>();
        collection.AddTransient<PreferencesWindowViewModel>();
        collection.AddTransient<SetupWindowViewModel>();
        collection.AddTransient<RecentBundlesViewModel>();
        collection.AddTransient<BundleChoiceViewModel>();
        collection.AddTransient<RelinkMediaViewModel>();

        collection.AddSingleton<MainWindowViewModel>();
    }
}