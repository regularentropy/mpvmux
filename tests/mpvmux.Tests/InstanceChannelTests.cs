using mpvmux.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace mpvmux.Tests;

public sealed class InstanceChannelTests : IDisposable
{
    private readonly string _rootPath;

    public InstanceChannelTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "mpvmux-channel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootPath);
    }

    private static bool UseNamedPipe => OperatingSystem.IsWindows();

    private string NewEndpoint() => InstanceChannel.EndpointFor(Path.Combine(_rootPath, Guid.NewGuid().ToString("N")), UseNamedPipe);

    private InstanceChannel NewChannel(string endpoint, int attempts = InstanceChannel.DefaultAttempts) =>
        new(endpoint, UseNamedPipe, attempts);

    // ---- payload --------------------------------------------------------------

    [Theory]
    [InlineData("/media/episode-01.mpvm", true)]
    [InlineData("/media/Episode-01.MPVM", true)]
    [InlineData("episode-01.mpvm", false)]
    [InlineData("/media/episode-01.mkv", false)]
    [InlineData("/media/episode-01.mpvm.bak", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void OnlyRootedBundlePathsAreAccepted(string message, bool expected)
    {
        Assert.Equal(expected, InstanceChannel.TryParseBundlePath(message, out _));
    }

    [Fact]
    public void AWindowsStylePathIsRootedOnlyOnWindows()
    {
        var rooted = InstanceChannel.TryParseBundlePath(@"C:\media\episode-01.mpvm", out _);

        Assert.Equal(OperatingSystem.IsWindows(), rooted);
    }

    // ---- endpoint -------------------------------------------------------------

    [Fact]
    public void TheEndpointIsStableForADataFolder()
    {
        var folder = Path.Combine(_rootPath, "data");

        Assert.Equal(
            InstanceChannel.EndpointFor(folder, UseNamedPipe),
            InstanceChannel.EndpointFor(folder, UseNamedPipe));
    }

    [Fact]
    public void DifferentDataFoldersGetDifferentEndpoints()
    {
        Assert.NotEqual(
            InstanceChannel.EndpointFor(Path.Combine(_rootPath, "first"), UseNamedPipe),
            InstanceChannel.EndpointFor(Path.Combine(_rootPath, "second"), UseNamedPipe));
    }

    // ---- round trip -----------------------------------------------------------

    [Fact]
    public async Task ABundlePathReachesTheListener()
    {
        var endpoint = NewEndpoint();
        using var listenerChannel = NewChannel(endpoint);
        using var senderChannel = NewChannel(endpoint);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var listening = listenerChannel.RunAsync(
            path =>
            {
                received.TrySetResult(path);
                return Task.CompletedTask;
            },
            cancellation.Token);

        var bundlePath = Path.Combine(_rootPath, "episode-01.mpvm");

        Assert.True(await senderChannel.SendBundlePathAsync(bundlePath, cancellation.Token));
        Assert.Equal(bundlePath, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        await StopAsync(listening, cancellation);
    }

    [Fact]
    public async Task SendingWithoutAListenerReportsFailure()
    {
        using var senderChannel = NewChannel(NewEndpoint(), attempts: 1);

        Assert.False(await senderChannel.SendBundlePathAsync(Path.Combine(_rootPath, "episode-01.mpvm")));
    }

    [Fact]
    public async Task SendingRejectsAPathThatIsNotABundle()
    {
        using var senderChannel = NewChannel(NewEndpoint(), attempts: 1);

        Assert.False(await senderChannel.SendBundlePathAsync(Path.Combine(_rootPath, "episode-01.mkv")));
    }

    // ---- startup race ---------------------------------------------------------

    [Fact]
    public async Task ARetryConnectsWhenTheListenerStartsLate()
    {
        var endpoint = NewEndpoint();
        using var listenerChannel = NewChannel(endpoint);
        using var senderChannel = NewChannel(endpoint);

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var bundlePath = Path.Combine(_rootPath, "episode-01.mpvm");

        // the first attempt happens before the listener exists, like a launch during startup
        var sending = senderChannel.SendBundlePathAsync(bundlePath, cancellation.Token);
        await Task.Delay(150, cancellation.Token);

        var listening = listenerChannel.RunAsync(
            path =>
            {
                received.TrySetResult(path);
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.True(await sending);
        Assert.Equal(bundlePath, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        await StopAsync(listening, cancellation);
    }

    [Fact]
    public async Task DisposingASenderChannelLeavesTheListenersEndpointAlone()
    {
        if (UseNamedPipe)
            return;

        var endpoint = NewEndpoint();
        using var listenerChannel = NewChannel(endpoint);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var listening = listenerChannel.RunAsync(_ => Task.CompletedTask, cancellation.Token);
        Assert.True(File.Exists(endpoint));

        // a second launch only sends; dropping its channel must not unlink the running socket
        var sender = NewChannel(endpoint, attempts: 1);
        sender.Dispose();

        Assert.True(File.Exists(endpoint));

        await StopAsync(listening, cancellation);
    }

    private static async Task StopAsync(Task listening, CancellationTokenSource cancellation)
    {
        cancellation.Cancel();

        try
        {
            await listening.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // the listener is best-effort; a stuck accept must not fail the assertion above
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp directory must not fail the test run
        }
    }
}
