using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using mpvmux.Constants;
using mpvmux.Services;
using mpvmux.ViewModels;
using mpvmux.Views;
using System;
using System.IO;
using System.Net.Http;

namespace mpvmux;

public partial class App : Application
{
    private static readonly Uri LightTrayIconUri = new("avares://mpvmux/Assets/taskbar-light.ico");
    private static readonly Uri DarkTrayIconUri = new("avares://mpvmux/Assets/taskbar-dark.ico");

    private IConfigService _configService;
    private IHistoryService _historyService;
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

            var collection = new ServiceCollection();
            collection.AddCommonServices();

            var services = collection.BuildServiceProvider();

            _historyService = services.GetRequiredService<IHistoryService>();
            _configService = services.GetRequiredService<IConfigService>();

            ThemeManager.Apply(_configService.Config.UseDarkTheme);

            var vm = services.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow();
            mainWindow.DataContext = vm;

            desktop.MainWindow = mainWindow;

            mainWindow.Loaded += async (_, _) => await vm.InitializeAsync();

            mainWindow.Closing += OnWindowClosing;

            CreateTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnWindowClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        _historyService.SaveHistory();
        _configService.Save();
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

        var quitItem = new NativeMenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Quit();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(_autoplayMenuItem);
        menu.Add(_fullscreenMenuItem);
        menu.Add(quitItem);

        // preferences can change the settings while this menu is closed
        menu.NeedsUpdate += (_, _) =>
        {
            _autoplayMenuItem.IsChecked = _configService.Config.EnableAutomaticPlayback;
            _fullscreenMenuItem.IsChecked = _configService.Config.EnableFullscreen;
        };

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
        _autoplayMenuItem!.IsChecked = _configService.Config.EnableAutomaticPlayback;
    }

    private void ToggleFullscreen()
    {
        _configService.Config.EnableFullscreen = !_configService.Config.EnableFullscreen;
        _configService.Save();
        _fullscreenMenuItem!.IsChecked = _configService.Config.EnableFullscreen;
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
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<IBundleBackupStore, BundleBackupStore>();
        collection.AddSingleton<IBundleRepository, BundleRepository>();
        collection.AddSingleton<IUpdateService, UpdateService>();
        collection.AddSingleton<IBundleFileService, BundleFileService>();
        collection.AddSingleton<IPlayerService, PlayerService>();
        collection.AddSingleton<IDialogHelper, DialogHelper>();

        // one long-lived client: sockets are reused instead of a pool per update check
        collection.AddSingleton(new HttpClient());

        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<IFilePickerService, FilePickerService>();
        collection.AddSingleton<IConfigService, ConfigService>();
        collection.AddSingleton<IMpvLocator, MpvLocator>();
        collection.AddSingleton<IBundleRelinkService, BundleRelinkService>();
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

        collection.AddSingleton<MainWindowViewModel>();
    }
}