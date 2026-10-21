using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal enum MpvProbeStatus
{
    Found,
    NotFound,
    NotExecutable,
    NotMpv,
}

internal sealed record MpvProbeResult(MpvProbeStatus Status, string? ExecutablePath = null, string? Version = null)
{
    public bool IsFound => Status == MpvProbeStatus.Found;
}

internal interface IMpvLocator
{
    /// <summary>Looks for a working mpv in the usual places (app folder, PATH, known installs).</summary>
    Task<MpvProbeResult> LocateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves and verifies a configured value: an executable, the folder containing it, or a
    /// bare command name that is looked up on PATH.
    /// </summary>
    Task<MpvProbeResult> ResolveAsync(string? path, CancellationToken cancellationToken = default);
}

internal sealed partial class MpvLocator : IMpvLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    // mpv.exe is a GUI-subsystem binary on Windows, so it may not write --version output to a
    // redirected stream; an empty output is accepted only for a file actually named mpv.
    private static readonly string[] ExecutableNames =
        OperatingSystem.IsWindows() ? ["mpv.exe", "mpv"] : ["mpv"];

    public async Task<MpvProbeResult> LocateAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in GetCandidatePaths())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // probing a missing file would only throw; PATH and the list of known installs
            // contain plenty of locations that do not exist on this machine
            if (!File.Exists(candidate))
                continue;

            var result = await ProbeAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (result.IsFound)
                return result;
        }

        return new MpvProbeResult(MpvProbeStatus.NotFound);
    }

    public Task<MpvProbeResult> ResolveAsync(string? path, CancellationToken cancellationToken = default)
    {
        var executable = ResolveExecutable(path);
        if (executable is null)
            return Task.FromResult(new MpvProbeResult(MpvProbeStatus.NotFound));

        return ProbeAsync(executable, cancellationToken);
    }

    /// <summary>
    /// Turns user input (or a config value) into a concrete executable path. Returns null when
    /// the value does not name an existing executable or folder.
    /// </summary>
    internal static string? ResolveExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        if (Directory.Exists(path))
            return FindExecutableInDirectory(path);

        if (File.Exists(path))
            return path;

        // a value with a separator is a path; anything else is a command name to look up
        if (path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar))
            return null;

        return FindOnPath(path);
    }

    internal static string? FindExecutableInDirectory(string directory)
    {
        try
        {
            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // unreadable folders are simply not a match
        }

        return null;
    }

    private static string? FindOnPath(string command)
    {
        string[] names = OperatingSystem.IsWindows() && !Path.HasExtension(command)
            ? [command, command + ".exe"]
            : [command];

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
            return null;

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length == 0)
                continue;

            foreach (var name in names)
            {
                var candidate = Path.Combine(trimmed, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var candidate in EnumerateCandidates())
        {
            if (seen.Add(candidate))
                yield return candidate;
        }
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        // a portable copy next to the app is picked up without any configuration
        yield return Path.Combine(AppContext.BaseDirectory, ExecutableNames[0]);
        yield return Path.Combine(AppContext.BaseDirectory, "mpv", ExecutableNames[0]);

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length > 0)
                yield return Path.Combine(trimmed, ExecutableNames[0]);
        }

        foreach (var known in GetKnownInstallLocations())
            yield return known;
    }

    private static IEnumerable<string> GetKnownInstallLocations()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[]
            {
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "mpv", "mpv.exe"),
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "mpv.exe"),
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "mpv", "mpv.exe"),
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "mpv", "mpv.exe"),
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "mpv.exe"),
                CombineIfSet(Environment.GetEnvironmentVariable("ProgramData"), "chocolatey", "bin", "mpv.exe"),
                CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "mpv", "mpv.exe"),
                @"C:\mpv\mpv.exe",
            })
            {
                if (candidate is not null)
                    yield return candidate;
            }

            yield break;
        }

        foreach (var candidate in new[]
        {
            "/usr/bin/mpv",
            "/usr/local/bin/mpv",
            "/snap/bin/mpv",
            "/opt/homebrew/bin/mpv",
            "/Applications/mpv.app/Contents/MacOS/mpv",
            CombineIfSet(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "mpv"),
        })
        {
            if (candidate is not null)
                yield return candidate;
        }
    }

    private static string? CombineIfSet(string? root, params string[] parts)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        return Path.Combine([root, .. parts]);
    }

    private static async Task<MpvProbeResult> ProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null)
                return new MpvProbeResult(MpvProbeStatus.NotExecutable, executablePath);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new MpvProbeResult(MpvProbeStatus.NotExecutable, executablePath);
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false)
                + await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
                return new MpvProbeResult(MpvProbeStatus.NotMpv, executablePath);

            if (string.IsNullOrWhiteSpace(output))
            {
                return IsMpvFileName(executablePath)
                    ? new MpvProbeResult(MpvProbeStatus.Found, executablePath)
                    : new MpvProbeResult(MpvProbeStatus.NotMpv, executablePath);
            }

            if (!LooksLikeMpv(output))
                return new MpvProbeResult(MpvProbeStatus.NotMpv, executablePath);

            return new MpvProbeResult(MpvProbeStatus.Found, executablePath, ParseVersion(output));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new MpvProbeResult(MpvProbeStatus.NotExecutable, executablePath);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // the probe already timed out; nothing useful is left to do
        }
    }

    private static bool LooksLikeMpv(string output)
    {
        var firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return firstLine is not null
            && firstLine.TrimStart().StartsWith("mpv ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionPattern().Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsMpvFileName(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "mpv", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "mpv.exe", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^mpv\s+v?(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
