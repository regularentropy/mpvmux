using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class TopMenuViewModel : ViewModelBase
{
    private readonly IConfigService _configRepositoryService;
    private readonly IUpdateService _updateService;
    private readonly IWindowService _windowService;
    private readonly IDialogHelper _dialogHelper;
    private readonly MediaContext _mediaContext;

    public TopMenuViewModel(
        IConfigService cs,
        IWindowService sv,
        IDialogHelper dh,
        IUpdateService us,
        MediaContext mc,
        FileMenuViewModel fileMenu,
        HistoryViewModel history)
    {
        _configRepositoryService = cs;
        _windowService = sv;
        _dialogHelper = dh;
        _updateService = us;
        _mediaContext = mc;

        FileMenu = fileMenu;
        History = history;
    }

    public FileMenuViewModel FileMenu { get; }
    public HistoryViewModel History { get; }

    public MediaContext MediaContext => _mediaContext;

    public ConfigModel ConfigModel => _configRepositoryService.Config;

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

    [RelayCommand]
    private static void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }
}
