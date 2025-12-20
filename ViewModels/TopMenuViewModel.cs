using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using mpvmux.Views;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class TopMenuViewModel : ViewModelBase
{
    private readonly IConfigService _configRepositoryService;
    private readonly IHistoryService _historyService;
    private readonly IWindowService _windowService;
    private readonly IFilePickerService _filePickerService;
    private readonly IUpdateService _updateService;
    private readonly IBundleFileService _bundleFileService;

    [ObservableProperty]
    private MediaRecord _selectedItem;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isHistoryExists = false;

    [ObservableProperty]
    private ConfigModel _configModel = new();

    [ObservableProperty]
    private HistoryModel _historyModel = new();

    public TopMenuViewModel(
        IConfigService cs,
        IHistoryService hs,
        IWindowService sv,
        IUpdateService us,
        IFilePickerService fps,
        IBundleFileService bfs)
    {
        _configRepositoryService = cs;
        _historyService = hs;
        _windowService = sv;
        _filePickerService = fps;
        _updateService = us;
        _bundleFileService = bfs;

        _historyService.OnHistoryChanged += RefreshHistory;

        ConfigModel = ConfigModel.FromDto(_configRepositoryService.Config);
        HistoryModel = HistoryModel.FromDto(_historyService.HistoryModel);

        if (HistoryModel.History.Count > 0)
        {
            IsHistoryExists = true;
        }

        ConfigModel.PropertyChanged += UpdateConfig;
    }

    async partial void OnSelectedIndexChanged(int value)
    {
        if (value < 0 || value >= HistoryModel.History.Count) return;

        try
        {
            await _bundleFileService.LoadAsync(HistoryModel.History[value].Path);
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error", $"Failed to load file: {ex.Message}");
        }
    }

    private void RefreshHistory(object? sender, EventArgs e)
    {
        HistoryModel = HistoryModel.FromDto(_historyService.HistoryModel);
        if (HistoryModel.History.Count > 0)
        {
            IsHistoryExists = true;
        }
    }

    private async void UpdateConfig(object? sender, EventArgs e)
    {
        try
        {
            await _configRepositoryService.SaveAsync(ConfigModel.ToDto());
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error", $"Failed to save config: {ex.Message}");
        }
    }

    private void ShowErrorDialog(string title, string message)
    {
        _windowService.ShowDialog(() => new MessageBoxWindow(title, message));
    }

    public void UpdateSelectedIndexByPath(string path)
    {
        SelectedIndex = _historyService.HistoryModel.History
            .FindIndex(x => x.Path == path);
    }

    [RelayCommand]
    private async Task DeleteHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        int indexToDelete = HistoryModel.History.IndexOf(record);
        
        await _historyService.RemoveEntryAsync(record);

        if (HistoryModel.History.Count == 0)
        {
            IsHistoryExists = false;
            SelectedIndex = -1;
        }
        else
        {
            if (indexToDelete >= HistoryModel.History.Count)
            {
                SelectedIndex = HistoryModel.History.Count - 1;
            }
            else
            {
                SelectedIndex = indexToDelete;
            }
        }
    }

    [RelayCommand]
    private void ShowAboutMenu()
    {
        _windowService.ShowDialog<AboutWindow, AboutWindowViewModel>();
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        try
        {
            var result = await _updateService.CheckForUpdates();
            if (result is null)
            {
                ShowErrorDialog("Updater", "No updates found");
            }
            else
            {
                _windowService.GetDialog<UpdaterWindow, UpdaterViewModel>(() => new UpdaterViewModel(result)).Show();
            }
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            ShowErrorDialog("Updater", "Cannot access the internet. Please check your connection.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ShowErrorDialog("Updater", "The software repository is unavailable.");
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Updater", $"An unexpected error occurred: {ex.Message}");
        }
    }

    [RelayCommand]
    private static void Exit()
    {
        Environment.Exit(0);
    }

    [RelayCommand]
    private async Task NewFile()
    {
        var path = await _filePickerService.SaveFilePickerAsync();
        if (path == null) return;
        await _bundleFileService.NewFileAsync();
        await _bundleFileService.SaveAsAsync(path);
        UpdateSelectedIndexByPath(path);
    }

    [RelayCommand]
    private async Task LoadFile()
    {
        var path = await _filePickerService.OpenFilePickerAsync();
        if (path == null) return;
        await _bundleFileService.LoadAsync(path);
        UpdateSelectedIndexByPath(path);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        var path = await _filePickerService.SaveFilePickerAsync();
        if (path == null) return;
        await _bundleFileService.SaveAsAsync(path);
        UpdateSelectedIndexByPath(path);
    }

    [RelayCommand]
    private async Task Save()
    {
        await _bundleFileService.SaveAsync();
    }
}