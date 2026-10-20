using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace mpvmux.Services;

internal interface IPlayerService
{
    void LaunchMPV(string videoPath, string audioPath);
}

internal class PlayerService(MediaContext mc, IConfigService cs) : IPlayerService
{
    private Process? _currentProcess;

    public void LaunchMPV(string videoPath, string audioPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be empty", nameof(videoPath));

        var processArgs = new ProcessStartInfo(cs.Config.MPVPath)
        {
            UseShellExecute = false
        };

        if (cs.Config.EnableFullscreen)
        {
            processArgs.ArgumentList.Add("--fullscreen");
        }
        
        processArgs.ArgumentList.Add($"--volume={mc.Bundle.Volume}");

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

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (cs.Config.EnableAutomaticPlayback && mc.Bundle.TryIncrement())
                {
                    var nextRecord = mc.Bundle.SelectedRecord;
                    LaunchMPV(nextRecord.Name, nextRecord.Path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-play failed: {ex}");
            }
        });
    }
}