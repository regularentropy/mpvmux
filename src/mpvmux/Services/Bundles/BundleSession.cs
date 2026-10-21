using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Models;

namespace mpvmux.Services;

internal partial class BundleSession : ObservableObject
{
    [ObservableProperty] private MediaBundleModel _bundle = new();
}