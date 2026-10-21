using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System;
using System.Diagnostics;

namespace mpvmux.ViewModels;

internal partial class AboutWindowViewModel : ViewModelBase
{
    public string SoftwareName { get; set; }
    public string Version { get; set; }
    public string Copyright { get; set; }
    private string RepositoryLink { get; set; }

    public AboutWindowViewModel(AppMetadata appMetadata)
    {
        SoftwareName = appMetadata.Title;
        Version = $"v{appMetadata.Version}";
        Copyright = $"© {DateTime.Now.Year} {appMetadata.Author}";
        RepositoryLink = appMetadata.RepositoryLink;
    }

    [RelayCommand]
    private void OpenRepositoryLink()
    {
        Process.Start(new ProcessStartInfo(RepositoryLink) { UseShellExecute = true });
    }
}
