using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;
using mpvmux.Models;

namespace mpvmux.Services;

internal interface IPlayerService
{
    bool IsPlaying { get; }

    void LaunchMPV(MediaBundleModel bundle);

    void Stop();

    event EventHandler? IsPlayingChanged;
}

internal class PlayerService(BundleSession bundleSession, IConfigService configService) : IPlayerService
{
    private Process? _currentProcess;

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

        var process = new Process { StartInfo = processArgs };
        process.Exited += OnMpvExited;
        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new InvalidOperationException("Failed to start MPV process");

        _currentProcess = process;
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
        if (_currentProcess == process)
            _currentProcess = null;

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