using System;
using System.Diagnostics;

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

        processArgs.ArgumentList.Add(videoPath);
        processArgs.ArgumentList.Add($"--audio-file={audioPath}");

        _currentProcess = Process.Start(processArgs);

        if (_currentProcess == null)
            throw new InvalidOperationException("Failed to start MPV process");

        _currentProcess.EnableRaisingEvents = true;
        _currentProcess.Exited += OnMpvExited;
    }

    private void OnMpvExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
            return;

        process.Exited -= OnMpvExited;
        process.Dispose();

        if (cs.Config.EnableAutomaticPlayback && mc.Bundle.TryIncrement())
        {
            var nextRecord = mc.Bundle.SelectedRecord;
            LaunchMPV(nextRecord.Name, nextRecord.Path);
        }
    }
}