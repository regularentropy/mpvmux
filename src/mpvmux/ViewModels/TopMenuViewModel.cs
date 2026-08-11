using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Models;
using mpvmux.Services;
using mpvmux.Views;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

internal partial class TopMenuViewModel : ViewModelBase
{
    private readonly IConfigService _configRepositoryService;
    private readonly IHistoryService _historyService;
    private readonly IFilePickerService _filePickerService;
    private readonly IUpdateService _updateService;
    private readonly IBundleFileService _bundleFileService;
    private readonly IWindowService _windowService;
    private readonly IDialogHelper _dialogHelper;
    private readonly MediaContext _mediaContext;

    private int _loadGeneration;
    private bool _suppressLoad;

    public MediaContext MediaContext => _mediaContext;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isHistoryExists;

    [ObservableProperty]
    private ConfigModel _configModel = new();

    [ObservableProperty]
    private HistoryModel _historyModel = new();

    public TopMenuViewModel(
        IConfigService cs,
        IHistoryService hs,
        IWindowService sv,
        IDialogHelper dh,
        IUpdateService us,
        IFilePickerService fps,
        IBundleFileService bfs,
        MediaContext mc)
    {
        _configRepositoryService = cs;
        _historyService = hs;
        _windowService = sv;
        _dialogHelper = dh;
        _filePickerService = fps;
        _updateService = us;
        _bundleFileService = bfs;
        _mediaContext = mc;

        _historyService.OnHistoryChanged += RefreshHistory;

        ConfigModel = ConfigModel.FromDto(_configRepositoryService.Config);
        HistoryModel = HistoryModel.FromDto(_historyService.HistoryModel);
        IsHistoryExists = HistoryModel.History.Count > 0;

        ConfigModel.PropertyChanged += UpdateConfig;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (_suppressLoad) return;
        if (value < 0 || value >= HistoryModel.History.Count) return;

        _ = LoadSelectedAsync(HistoryModel.History[value].Path);
    }

    private async Task LoadSelectedAsync(string path)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);

        try
        {
            await _bundleFileService.LoadAsync(path);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _loadGeneration)) return;
            _dialogHelper.ShowError($"Failed to load file: {ex.Message}");
        }
    }

    private void SetSelectedIndexSilently(int index)
    {
        _suppressLoad = true;
        try { SelectedIndex = index; }
        finally { _suppressLoad = false; }
    }

    public void UpdateSelectedIndexByPath(string path)
    {
        var index = -1;
        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            if (string.Equals(HistoryModel.History[i].Path, path, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        SetSelectedIndexSilently(index);
    }

    private void RefreshHistory(object? sender, EventArgs e)
    {
        HistoryModel = HistoryModel.FromDto(_historyService.HistoryModel);
        IsHistoryExists = HistoryModel.History.Count > 0;
    }

    private async void UpdateConfig(object? sender, EventArgs e)
    {
        try
        {
            await _configRepositoryService.UpdateConfig(ConfigModel.ToDto());
        }
        catch (Exception ex)
        {
            _dialogHelper.ShowError($"Failed to save config: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        var indexToDelete = HistoryModel.History.IndexOf(record);

        await _historyService.RemoveEntryAsync(record);

        if (HistoryModel.History.Count == 0)
        {
            IsHistoryExists = false;
            SetSelectedIndexSilently(-1);
        }
        else
        {
            var next = indexToDelete >= HistoryModel.History.Count
                ? HistoryModel.History.Count - 1
                : indexToDelete;

            SetSelectedIndexSilently(next);
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
                _dialogHelper.ShowError("No updates found");
            }
            else
            {
                _windowService.GetDialog<UpdaterWindow, UpdaterViewModel>(() => new UpdaterViewModel(result)).Show();
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