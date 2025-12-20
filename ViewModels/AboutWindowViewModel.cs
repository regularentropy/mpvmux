using CommunityToolkit.Mvvm.Input;
using mpvmux.Services;
using System.Diagnostics;

namespace mpvmux.ViewModels;

internal partial class AboutWindowViewModel : ViewModelBase
{
    public string SoftwareName { get; set; }
    public string Version { get; set; }
    private string RepositoryLink { get; set; }

    public AboutWindowViewModel(MetadataRepository sms)
    {
        SoftwareName = sms.Title;
        Version = sms.Version.ToString();
        RepositoryLink = sms.RepositoryLink;
    }

    [RelayCommand]
    private void OpenRepositoryLink()
    {
        Process.Start(new ProcessStartInfo(RepositoryLink) { UseShellExecute = true });
    }
}