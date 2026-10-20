using System;
using System.Diagnostics;
using Avalonia.Threading;
using mpvmux.Models;

namespace mpvmux.Services;

internal interface IPlayerService
{
    void LaunchMPV(MediaBundleModel bundle);

    event EventHandler? PlaybackEnded;
}

internal class PlayerService(MediaContext mc, IConfigService cs) : IPlayerService
{
    private Process? _currentProcess;

    public event EventHandler? PlaybackEnded;

    public void LaunchMPV(MediaBundleModel bundle)
    {
        if (!bundle.TryGetSelectedPaths(out var videoPath, out var audioPath))
            throw new ArgumentException("No playable video/audio pair selected", nameof(bundle));
        
        // allow only one mpv process
        if (_currentProcess is not null)
            return;

        var processArgs = new ProcessStartInfo(cs.Config.MpvPath)
        {
            UseShellExecute = false
        };

        if (cs.Config.EnableFullscreen)
        {
            processArgs.ArgumentList.Add("--fullscreen");
        }
        
        processArgs.ArgumentList.Add($"--volume={bundle.Volume}");

        processArgs.ArgumentList.Add(videoPath);
        processArgs.ArgumentList.Add($"--audio-file={audioPath}");

        var process = new Process { StartInfo = processArgs };
        process.Exited += OnMpvExited;
        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new InvalidOperationException("Failed to start MPV process");

        _currentProcess = process;
    }

    private void OnMpvExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        process.Exited -= OnMpvExited;
        process.Dispose();
        if (_currentProcess == process)
            _currentProcess = null;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (cs.Config.EnableAutomaticPlayback && mc.Bundle.TryIncrement())
                {
                    LaunchMPV(mc.Bundle);
                    if (_currentProcess is not null)
                        return;
                }
            }
            catch (Exception) { }

            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        });
    }
}