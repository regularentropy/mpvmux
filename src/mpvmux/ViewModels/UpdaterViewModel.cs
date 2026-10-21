using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.Diagnostics;

namespace mpvmux.ViewModels;

internal partial class UpdaterViewModel : ViewModelBase
{
    private readonly string _releaseLink;

    public UpdaterViewModel(UpdateResponse resp, AppMetadata metadata)
    {
        Heading = "A new version is available";
        NewVersion = $"v{resp.Tag}";
        CurrentVersion = $"v{metadata.Version}";
        VersionSummary = $"You are running {CurrentVersion}.";
        ReleaseNotes = string.IsNullOrWhiteSpace(resp.ReleaseNotes)
            ? "No release notes were provided."
            : resp.ReleaseNotes.Trim();
        _releaseLink = resp.Url;
    }

    public string Heading { get; }
    public string NewVersion { get; }
    public string CurrentVersion { get; }
    public string VersionSummary { get; }

    public string ReleaseNotes { get; }

    [RelayCommand]
    private void OpenUpdateLink()
    {
        if (string.IsNullOrWhiteSpace(_releaseLink))
            return;

        Process.Start(new ProcessStartInfo(_releaseLink) { UseShellExecute = true });
    }
}
