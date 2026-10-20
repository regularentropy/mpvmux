using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly IVideoRepositoryService _videoRepositoryService;
    private readonly IWindowService _windowService;
    private readonly IDialogHelper _dialogHelper;
    private readonly MediaContext _mediaContext;

    private int _loadGeneration;
    private bool _suppressLoad;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public MediaContext MediaContext => _mediaContext;

    [ObservableProperty]
    private int _selectedIndex = -1;

    [ObservableProperty]
    private bool _isHistoryExists;

    public ConfigModel ConfigModel => _configRepositoryService.Config;

    public HistoryModel HistoryModel => _historyService.HistoryModel;

    public TopMenuViewModel(
        IConfigService cs,
        IHistoryService hs,
        IWindowService sv,
        IDialogHelper dh,
        IUpdateService us,
        IFilePickerService fps,
        IBundleFileService bfs,
        IVideoRepositoryService vrs,
        MediaContext mc)
    {
        _configRepositoryService = cs;
        _historyService = hs;
        _windowService = sv;
        _dialogHelper = dh;
        _filePickerService = fps;
        _updateService = us;
        _bundleFileService = bfs;
        _videoRepositoryService = vrs;
        _mediaContext = mc;

        _historyService.OnHistoryChanged += RefreshHistory;

        IsHistoryExists = HistoryModel.History.Count > 0;
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

        await _loadLock.WaitAsync();
        try
        {
            if (generation != Volatile.Read(ref _loadGeneration)) return;

            await _bundleFileService.LoadAsync(path);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _loadGeneration)) return;
            _dialogHelper.ShowError($"Failed to load file: {ex.Message}");
        }
        finally
        {
            _loadLock.Release();
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
        SetSelectedIndexSilently(IndexOfPath(path));
    }

    private int IndexOfPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return -1;

        for (var i = 0; i < HistoryModel.History.Count; i++)
        {
            if (string.Equals(HistoryModel.History[i].Path, path, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void RefreshHistory(object? sender, EventArgs e)
    {
        _suppressLoad = true;
        try
        {
            IsHistoryExists = HistoryModel.History.Count > 0;
            SelectedIndex = IndexOfPath(_videoRepositoryService.SelectedFilePath);
        }
        finally
        {
            _suppressLoad = false;
        }
    }

    [RelayCommand]
    private async Task DeleteHistoryRecord(MediaRecord record)
    {
        if (record == null) return;

        var indexToDelete = HistoryModel.History.IndexOf(record);
        var wasLoaded = string.Equals(_videoRepositoryService.SelectedFilePath, record.Path, StringComparison.OrdinalIgnoreCase);
        
        _suppressLoad = true;
        try
        {
            await _historyService.RemoveEntryAsync(record);
        }
        finally
        {
            _suppressLoad = false;
        }

        // RefreshHistory has already re-pointed SelectedIndex at the loaded file (-1 if it is gone)
        if (!wasLoaded) return;

        if (HistoryModel.History.Count == 0)
        {
            // nothing left to show: drop the bundle and its save target, or Ctrl+S recreates the deleted file
            await _bundleFileService.NewFileAsync();
            return;
        }

        var next = Math.Min(indexToDelete, HistoryModel.History.Count - 1);
        SetSelectedIndexSilently(next);
        await LoadSelectedAsync(HistoryModel.History[next].Path);
    }

    [RelayCommand]
    private void ShowPreferences()
    {
        _windowService.ShowDialog<PreferencesWindow, PreferencesWindowViewModel>();
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
                _dialogHelper.ShowInfo("No updates found");
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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
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
    private Task Save()
    {
        if (string.IsNullOrEmpty(_videoRepositoryService.SelectedFilePath))
            return SaveAs();

        return _bundleFileService.SaveAsync();
    }
}