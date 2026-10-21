using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace mpvmux.ViewModels;

/// <summary>
/// First-run helper: finds mpv automatically, and otherwise lets the user point at the folder or
/// executable and verifies the choice before saving it.
/// </summary>
internal partial class SetupWindowViewModel : ViewModelBase
{
    private const string DownloadUrl = "https://mpv.io/installation/";

    private readonly IConfigService _configService;
    private readonly IMpvLocator _mpvLocator;
    private readonly IFilePickerService _filePickerService;

    public SetupWindowViewModel(
        IConfigService configService,
        IMpvLocator mpvLocator,
        IFilePickerService filePickerService)
    {
        _configService = configService;
        _mpvLocator = mpvLocator;
        _filePickerService = filePickerService;

        _mpvPath = configService.Config.MpvPath;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "Looking for mpv...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusDetail))]
    private string _statusDetail = "";

    [ObservableProperty] private bool _isStatusError;

    [ObservableProperty] private string _mpvPath;

    public bool HasStatusDetail => !string.IsNullOrWhiteSpace(StatusDetail);

    public bool CanInteract => !IsBusy;

    /// <summary>Resolves the configured value first, then searches the usual install locations.</summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SetStatus("Looking for mpv...", "", isError: false);

        try
        {
            var result = await _mpvLocator.ResolveAsync(_configService.Config.MpvPath);
            if (!result.IsFound)
                result = await _mpvLocator.LocateAsync();

            if (result.IsFound)
            {
                ApplyFound(result);
                return;
            }

            SetStatus(
                "mpv was not found automatically.",
                "Install mpv, or choose the folder that contains mpv.exe (or the executable itself).",
                isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var path = await _filePickerService.PickFolderAsync();
        if (path is not null)
            await UsePathAsync(path);
    }

    [RelayCommand]
    private async Task BrowseExecutable()
    {
        var path = await _filePickerService.PickExecutableAsync();
        if (path is not null)
            await UsePathAsync(path);
    }

    [RelayCommand]
    private void OpenDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // no default browser is not worth an error dialog here
        }
    }

    /// <summary>Validates the entered path and persists it. Returns false when it is unusable.</summary>
    public async Task<bool> TrySaveAsync()
    {
        if (IsBusy)
            return false;

        var path = MpvPath?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("No mpv location was chosen.", "Choose the folder or executable first.", isError: true);
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await _mpvLocator.ResolveAsync(path);
            if (!result.IsFound)
            {
                ShowProbeError(result);
                return false;
            }

            ApplyFound(result);
            _configService.Config.MpvPath = result.ExecutablePath!;
            _configService.Config.MpvSetupCompleted = true;
            _configService.Save();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Closing the window (including the title bar button) counts as "not now", so the setup
    /// does not reappear on every start. It stays reachable from the File menu.
    /// </summary>
    public void Dismiss()
    {
        if (_configService.Config.MpvSetupCompleted)
            return;

        _configService.Config.MpvSetupCompleted = true;
        _configService.Save();
    }

    private async Task UsePathAsync(string path)
    {
        MpvPath = path;

        if (IsBusy)
            return;

        IsBusy = true;
        SetStatus("Checking the selected location...", "", isError: false);

        try
        {
            var result = await _mpvLocator.ResolveAsync(path);
            if (result.IsFound)
                ApplyFound(result);
            else
                ShowProbeError(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFound(MpvProbeResult result)
    {
        MpvPath = result.ExecutablePath!;

        var version = string.IsNullOrWhiteSpace(result.Version) ? "" : $" {result.Version}";
        SetStatus($"mpv{version} is ready.", result.ExecutablePath!, isError: false);
    }

    private void ShowProbeError(MpvProbeResult result)
    {
        switch (result.Status)
        {
            case MpvProbeStatus.NotFound:
                SetStatus(
                    "No mpv executable was found there.",
                    "Pick the folder that contains mpv.exe, or install mpv first.",
                    isError: true);
                break;
            case MpvProbeStatus.NotExecutable:
                SetStatus("That file could not be started.", "Choose mpv.exe, or the folder containing it.", isError: true);
                break;
            case MpvProbeStatus.NotMpv:
                SetStatus("That file did not respond like mpv.", "Choose the mpv executable itself.", isError: true);
                break;
            default:
                SetStatus("mpv could not be used.", "Choose another location.", isError: true);
                break;
        }
    }

    private void SetStatus(string text, string detail, bool isError)
    {
        StatusText = text;
        StatusDetail = detail;
        IsStatusError = isError;
    }
}
