using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Models;

namespace mpvmux.Services;

internal partial class MediaContext : ObservableObject
{
    [ObservableProperty] private MediaBundleModel _bundle = new();
}