using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using mpvmux.Models;

namespace mpvmux.Services;

internal interface IPlayerService
{
    bool IsPlaying { get; }

    void LaunchMPV(MediaBundleModel bundle);

    /// <summary>Toggles playback pause through mpv's IPC endpoint.</summary>
    Task PauseAsync();

    void Stop();

    /// <summary>Stops playback and completes once mpv has exited.</summary>
    Task StopAsync();

    event EventHandler? IsPlayingChanged;
}

internal class PlayerService(BundleSession bundleSession, IConfigService configService) : IPlayerService
{
    private const string PauseCommand = """{"command":["cycle","pause"]}""";

    private Process? _currentProcess;

    // mpv's IPC endpoint for the current process, null when nothing is playing
    private string? _ipcPath;

    // completes when the current process exits; shared by everyone waiting for it
    private readonly object _exitGate = new();
    private TaskCompletionSource? _exitSignal;

    // set by a user-initiated Stop so mpv exiting is not mistaken for "track finished"
    private volatile bool _stopRequested;

    public bool IsPlaying => _currentProcess is not null;

    public event EventHandler? IsPlayingChanged;

    public void LaunchMPV(MediaBundleModel bundle)
    {
        if (!bundle.TryGetSelectedTracks(out _, out _, out _))
            throw new ArgumentException("No playable video selected", nameof(bundle));
        
        // allow only one mpv process
        if (_currentProcess is not null)
            return;

        var processArgs = new ProcessStartInfo(configService.Config.MpvPath)
        {
            UseShellExecute = false
        };

        foreach (var argument in BuildArguments(bundle, configService.Config.EnableFullscreen))
            processArgs.ArgumentList.Add(argument);

        // gives Pause a way to talk to the player; mpv creates the endpoint shortly after start
        var ipcPath = CreateIpcPath();
        processArgs.ArgumentList.Add($"--input-ipc-server={ipcPath}");

        var process = new Process { StartInfo = processArgs };
        process.Exited += OnMpvExited;
        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new InvalidOperationException("The mpv process could not be started.");

        _currentProcess = process;
        _ipcPath = ipcPath;
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static IReadOnlyList<string> BuildArguments(MediaBundleModel bundle, bool fullscreen)
    {
        if (!bundle.TryGetSelectedTracks(out var videoPath, out var audioPath, out var subtitlePath))
            throw new ArgumentException("No playable video selected", nameof(bundle));

        var arguments = new List<string>();

        if (fullscreen)
            arguments.Add("--fullscreen");

        // the track table is authoritative: do not let mpv pick up a sidecar subtitle
        // that is not in the grid. Embedded subtitles are still mpv's business.
        arguments.Add("--no-sub-auto");
        arguments.Add($"--volume={bundle.Volume}");
        arguments.Add(videoPath);

        if (audioPath is not null)
            arguments.Add($"--audio-file={audioPath}");
        if (subtitlePath is not null)
            arguments.Add($"--sub-file={subtitlePath}");

        return arguments;
    }

    public async Task PauseAsync()
    {
        var ipcPath = _ipcPath;
        if (ipcPath is null || _currentProcess is null)
            return;

        const int maxAttempts = 20;

        // mpv creates the endpoint shortly after being started, so a pause pressed
        // right after Play needs a few attempts before the connection succeeds
        for (var attempt = 0; ; attempt++)
        {
            if (_currentProcess is null)
                return;

            try
            {
                await SendIpcCommandAsync(ipcPath, PauseCommand).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts - 1 && ex is IOException or TimeoutException or SocketException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendIpcCommandAsync(string ipcPath, string command)
    {
        var payload = Encoding.UTF8.GetBytes(command + "\n");

        if (OperatingSystem.IsWindows())
        {
            const string pipePrefix = @"\\.\pipe\";
            var pipeName = ipcPath.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase)
                ? ipcPath[pipePrefix.Length..]
                : ipcPath;

            await using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(200).ConfigureAwait(false);
            await pipe.WriteAsync(payload).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);
            return;
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(ipcPath)).ConfigureAwait(false);

        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await stream.WriteAsync(payload).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string CreateIpcPath()
    {
        var name = "mpvmux-" + Guid.NewGuid().ToString("N");

        // mpv listens on a named pipe on Windows and a Unix socket elsewhere
        return OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{name}"
            : Path.Combine(Path.GetTempPath(), name + ".sock");
    }

    public Task StopAsync()
    {
        TaskCompletionSource? signal;

        lock (_exitGate)
        {
            if (_currentProcess is null)
                return Task.CompletedTask;

            signal = _exitSignal ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Stop();

        // do not leave a caller waiting forever if the process refuses to die
        return signal.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public void Stop()
    {
        var process = _currentProcess;
        if (process is null)
            return;

        _stopRequested = true;

        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
            // already exited; the exit handler still runs and clears the state
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no permission to kill; leave the process running rather than crash
        }
    }

    private void OnMpvExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        process.Exited -= OnMpvExited;
        process.Dispose();

        TaskCompletionSource? signal;

        lock (_exitGate)
        {
            if (_currentProcess == process)
            {
                _currentProcess = null;
                _ipcPath = null;
            }

            signal = _exitSignal;
            _exitSignal = null;
        }

        signal?.TrySetResult();

        var stopRequested = _stopRequested;
        _stopRequested = false;

        Dispatcher.UIThread.Post(() =>
        {
            if (!stopRequested)
            {
                try
                {
                    if (configService.Config.EnableAutomaticPlayback && bundleSession.Bundle.TryIncrement())
                    {
                        LaunchMPV(bundleSession.Bundle);
                        if (IsPlaying)
                            return;
                    }
                }
                catch (Exception) { }
            }

            // always raised on the UI thread, and IsPlaying already reflects the auto-play attempt
            IsPlayingChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}