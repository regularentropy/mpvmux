using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.Diagnostics;

namespace mpvmux.ViewModels;

internal partial class UpdaterViewModel : ViewModelBase
{
    public string ReleaseNotes { get; set; }

    public string ReleaseLink;
    public string ReleaseNotesTitle{ get; set; }

    public UpdaterViewModel(UpdateResponse resp)
    {
        ReleaseNotes = resp.ReleaseNotes;
        ReleaseLink = resp.Url;
        ReleaseNotesTitle = $"Release notes for v{resp.Tag}";
    }

    [RelayCommand]
    private void OpenUpdateLink()
    {
        Process.Start(new ProcessStartInfo(ReleaseLink) { UseShellExecute = true });
    }
}
