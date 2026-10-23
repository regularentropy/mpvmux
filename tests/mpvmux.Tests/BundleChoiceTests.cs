using mpvmux.ViewModels;
using System.Linq;
using Xunit;

namespace mpvmux.Tests;

public sealed class BundleChoiceTests
{
    [Fact]
    public void Initialize_PreselectsTheFirstDroppedBundle()
    {
        var viewModel = new BundleChoiceViewModel();

        viewModel.Initialize(["/media/first.mpvm", "/media/second.mpvm"]);

        Assert.Equal("/media/first.mpvm", viewModel.SelectedPath);
        Assert.Equal(["first", "second"], viewModel.Bundles.Select(bundle => bundle.Name));
        Assert.Equal("/media/second.mpvm", viewModel.Bundles[1].Path);
    }

    [Fact]
    public void SelectingAnotherBundle_ChangesTheChosenPath()
    {
        var viewModel = new BundleChoiceViewModel();
        viewModel.Initialize(["/media/first.mpvm", "/media/second.mpvm"]);

        viewModel.SelectedBundle = viewModel.Bundles[1];

        Assert.Equal("/media/second.mpvm", viewModel.SelectedPath);
    }

    [Fact]
    public void Initialize_ReplacesThePreviousDrop()
    {
        var viewModel = new BundleChoiceViewModel();
        viewModel.Initialize(["/media/first.mpvm", "/media/second.mpvm"]);

        viewModel.Initialize(["/media/third.mpvm"]);

        Assert.Equal(["third"], viewModel.Bundles.Select(bundle => bundle.Name));
        Assert.Equal("/media/third.mpvm", viewModel.SelectedPath);
    }
}
