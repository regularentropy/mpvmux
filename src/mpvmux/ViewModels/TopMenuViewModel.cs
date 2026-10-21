using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class TopMenuViewModel : ViewModelBase
{
    private readonly IUpdateService _updateService;
    private readonly IConfigService _configService;
    private readonly IWindowService _windowService;
    private readonly IDialogHelper _dialogHelper;
    private readonly BundleSession _bundleSession;
    private readonly IBundleRepository _bundleRepository;

    private int _volumeBeforeMute = 100;

    public TopMenuViewModel(
        IWindowService windowService,
        IDialogHelper dialogHelper,
        IUpdateService updateService,
        IConfigService configService,
        BundleSession bundleSession,
        IBundleRepository bundleRepository,
        FileMenuViewModel fileMenu,
        HistoryViewModel history)
    {
        _windowService = windowService;
        _dialogHelper = dialogHelper;
        _updateService = updateService;
        _configService = configService;
        _bundleSession = bundleSession;
        _bundleRepository = bundleRepository;

        _isDarkTheme = configService.Config.UseDarkTheme;

        FileMenu = fileMenu;
        History = history;

        _bundleRepository.SelectedFilePathChanged += OnSelectedFilePathChanged;
    }

    public FileMenuViewModel FileMenu { get; }
    public HistoryViewModel History { get; }

    public BundleSession Session => _bundleSession;

    [ObservableProperty]
    private bool _isDarkTheme;

    public string ThemeToggleToolTip => IsDarkTheme ? "Switch to light theme" : "Switch to dark theme";

    public bool HasBundleFolder => !string.IsNullOrEmpty(_bundleRepository.SelectedFilePath);

    partial void OnIsDarkThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(ThemeToggleToolTip));

        _configService.Config.UseDarkTheme = value;
        _configService.Save();
        ThemeManager.Apply(value);
    }

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    private void OnSelectedFilePathChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasBundleFolder));
        OpenBundleFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasBundleFolder))]
    private void OpenBundleFolder()
    {
        var path = _bundleRepository.SelectedFilePath;
        if (string.IsNullOrEmpty(path))
            return;

        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to open the folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        var bundle = Session.Bundle;
        if (bundle.Volume > 0)
        {
            _volumeBeforeMute = bundle.Volume;
            bundle.Volume = 0;
        }
        else
        {
            bundle.Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 100;
        }
    }

    [RelayCommand]
    private void ShowPreferences()
    {
        _windowService.ShowPreferences();
    }

    [RelayCommand]
    private void ShowAboutMenu()
    {
        _windowService.ShowAbout();
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        try
        {
            var result = await _updateService.CheckForUpdates();
            if (result is null)
            {
                _dialogHelper.ShowInfo("No updates found");
            }
            else
            {
                _windowService.ShowUpdater(result);
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            _dialogHelper.ShowError("Cannot access the internet. Please check your connection.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _dialogHelper.ShowError("The software repository is unavailable.");
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"An unexpected error occurred: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the optional check configured in Preferences. Unlike the manual command this is
    /// silent: errors and "no updates" never interrupt startup; only an available update is shown.
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_configService.Config.CheckForUpdatesOnStartup)
            return;

        try
        {
            var result = await _updateService.CheckForUpdates();
            if (result is not null)
                _windowService.ShowUpdater(result);
        }
        catch (Exception)
        {
            // a background check must not interrupt startup
        }
    }

    [RelayCommand]
    private static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }
}
