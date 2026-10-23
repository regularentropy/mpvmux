using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace mpvmux.ViewModels;

/// <summary>One bundle offered by the multi-drop picker: display name plus full path.</summary>
internal sealed record DroppedBundle(string Name, string Path);

/// <summary>
/// Backs the picker shown when a drop carries more than one bundle. The first dropped bundle
/// is preselected, mirroring the automatic open used for a single bundle; every bundle already
/// joins the recent list before the picker opens, so cancelling loses nothing.
/// </summary>
internal partial class BundleChoiceViewModel : ViewModelBase
{
    public ObservableCollection<DroppedBundle> Bundles { get; } = [];

    [ObservableProperty]
    private DroppedBundle? _selectedBundle;

    /// <summary>The chosen bundle's path, or null when nothing is selected.</summary>
    public string? SelectedPath => SelectedBundle?.Path;

    public void Initialize(IReadOnlyList<string> paths)
    {
        Bundles.Clear();

        foreach (var path in paths)
            Bundles.Add(new DroppedBundle(Path.GetFileNameWithoutExtension(path), path));

        SelectedBundle = Bundles.FirstOrDefault();
    }
}
