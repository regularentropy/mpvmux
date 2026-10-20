using System;
using System.Diagnostics;
using Avalonia.Threading;
using mpvmux.Models;

namespace mpvmux.Services;

internal interface IPlayerService
{
    bool IsPlaying { get; }

    void LaunchMPV(MediaBundleModel bundle);

    event EventHandler? IsPlayingChanged;
}

internal class PlayerService(BundleSession bundleSession, IConfigService configService) : IPlayerService
{
    private Process? _currentProcess;

    public bool IsPlaying => _currentProcess is not null;

    public event EventHandler? IsPlayingChanged;

    public void LaunchMPV(MediaBundleModel bundle)
    {
        if (!bundle.TryGetSelectedPaths(out var videoPath, out var audioPath))
            throw new ArgumentException("No playable video/audio pair selected", nameof(bundle));
        
        // allow only one mpv process
        if (_currentProcess is not null)
            return;

        var processArgs = new ProcessStartInfo(configService.Config.MpvPath)
        {
            UseShellExecute = false
        };

        if (configService.Config.EnableFullscreen)
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
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
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
                if (configService.Config.EnableAutomaticPlayback && bundleSession.Bundle.TryIncrement())
                {
                    LaunchMPV(bundleSession.Bundle);
                    if (IsPlaying)
                        return;
                }
            }
            catch (Exception) { }

            // always raised on the UI thread, and IsPlaying already reflects the auto-play attempt
            IsPlayingChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}