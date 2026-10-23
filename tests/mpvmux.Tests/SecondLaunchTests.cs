using mpvmux.Services;
using System.Collections.Generic;
using Xunit;

namespace mpvmux.Tests;

public sealed class SecondLaunchTests
{
    [Fact]
    public void ALaunchWithoutBundlesReportsThatMpvmuxIsRunning()
    {
        var attempted = new List<string>();

        var showMessage = SecondLaunch.ShouldShowAlreadyRunning([], path =>
        {
            attempted.Add(path);
            return true;
        });

        Assert.True(showMessage);
        Assert.Empty(attempted);
    }

    [Fact]
    public void ADeliveredBundleIsEnoughToFinishTheLaunch()
    {
        Assert.False(SecondLaunch.ShouldShowAlreadyRunning(["/media/episode-01.mpvm"], _ => true));
    }

    [Fact]
    public void AFailedDeliveryFallsBackToTheMessage()
    {
        Assert.True(SecondLaunch.ShouldShowAlreadyRunning(["/media/episode-01.mpvm"], _ => false));
    }

    [Fact]
    public void EveryBundleIsAttemptedEvenWhenTheFirstOneFails()
    {
        var attempted = new List<string>();

        var showMessage = SecondLaunch.ShouldShowAlreadyRunning(
            ["/media/first.mpvm", "/media/second.mpvm"],
            path =>
            {
                attempted.Add(path);
                return path.EndsWith("second.mpvm");
            });

        Assert.False(showMessage);
        Assert.Equal(["/media/first.mpvm", "/media/second.mpvm"], attempted);
    }
}
