using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>
/// The one-way channel a second launch uses to hand a bundle path to the running instance.
/// The endpoint is derived from the per-user data folder, so two users never share it: a named
/// pipe on Windows (<c>mpvmux-&lt;hash&gt;</c>), a Unix domain socket elsewhere
/// (<c>&lt;temp&gt;/mpvmux-&lt;hash&gt;.sock</c>). This mirrors how <see cref="PlayerService"/>
/// talks to mpv.
/// </summary>
internal interface IInstanceChannel
{
    /// <summary>
    /// Accepts messages until cancelled. Each accepted line is validated and, when it names a
    /// bundle, passed to <paramref name="onBundlePath"/>.
    /// </summary>
    Task RunAsync(Func<string, Task> onBundlePath, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a bundle path to the running instance. Completes with false when no instance
    /// accepted it, so the caller can fall back to saying the app is already running.
    /// </summary>
    Task<bool> SendBundlePathAsync(string path, CancellationToken cancellationToken = default);
}

internal sealed class InstanceChannel : IInstanceChannel, IDisposable
{
    public const string PipeNamePrefix = "mpvmux-";
    public const int DefaultAttempts = 5;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>A pathological path is not worth reading; bundle paths are far shorter.</summary>
    private const int MaxMessageLength = 4096;

    private readonly string _endpoint;
    private readonly bool _useNamedPipe;
    private readonly int _attempts;
    private readonly TimeSpan _retryDelay;
    private bool _listening;
    private bool _disposed;

    public InstanceChannel(
        string endpoint,
        bool useNamedPipe,
        int attempts = DefaultAttempts,
        TimeSpan? retryDelay = null)
    {
        _endpoint = endpoint;
        _useNamedPipe = useNamedPipe;
        _attempts = Math.Max(1, attempts);
        _retryDelay = retryDelay ?? DefaultRetryDelay;
    }

    /// <summary>The channel for the current user and this platform.</summary>
    public static InstanceChannel ForCurrentUser() =>
        new(EndpointFor(Constants.AppConstants.BaseFolderPath, OperatingSystem.IsWindows()), OperatingSystem.IsWindows());

    /// <summary>
    /// Stable per-user endpoint. The data folder already identifies the user, and hashing keeps
    /// the name short and free of characters a pipe name or socket path cannot carry.
    /// </summary>
    public static string EndpointFor(string dataFolderPath, bool useNamedPipe)
    {
        var normalized = Path.GetFullPath(dataFolderPath);

        // Windows paths are case-insensitive, so different spellings must land on one endpoint
        if (useNamedPipe)
            normalized = normalized.ToUpperInvariant();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant()[..16];

        return useNamedPipe
            ? PipeNamePrefix + hash
            : Path.Combine(Path.GetTempPath(), $"{PipeNamePrefix}{hash}.sock");
    }

    /// <summary>
    /// Accepts one line per message. Anything that is not a rooted path ending in
    /// <c>.mpvm</c> is ignored, so a stray writer cannot make the app open an arbitrary file.
    /// </summary>
    public static bool TryParseBundlePath(string? message, out string path)
    {
        path = "";

        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return false;

        var candidate = message.Trim();

        if (!Path.IsPathRooted(candidate))
            return false;

        if (!candidate.EndsWith(Constants.AppConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        path = candidate;
        return true;
    }

    public async Task RunAsync(Func<string, Task> onBundlePath, CancellationToken cancellationToken)
    {
        if (_useNamedPipe)
            await RunPipeAsync(onBundlePath, cancellationToken);
        else
            await RunSocketAsync(onBundlePath, cancellationToken);
    }

    private async Task RunPipeAsync(Func<string, Task> onBundlePath, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _endpoint,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                await DispatchAsync(line, onBundlePath).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SocketException)
            {
                // one broken connection must not stop the channel
            }
        }
    }

    private async Task RunSocketAsync(Func<string, Task> onBundlePath, CancellationToken cancellationToken)
    {
        _listening = true;
        DeleteStaleSocket();

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(_endpoint));
        listener.Listen(backlog: 4);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                using var stream = new NetworkStream(client, ownsSocket: false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                await DispatchAsync(line, onBundlePath).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
            {
                // one broken connection must not stop the channel
            }
        }
    }

    private static async Task DispatchAsync(string? line, Func<string, Task> onBundlePath)
    {
        if (TryParseBundlePath(line, out var path))
            await onBundlePath(path).ConfigureAwait(false);
    }

    public async Task<bool> SendBundlePathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!TryParseBundlePath(path, out var message))
            return false;

        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            if (await TrySendAsync(message, cancellationToken).ConfigureAwait(false))
                return true;

            if (attempt < _attempts)
            {
                try
                {
                    await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// One connect attempt. The running instance may still be starting its listener when a
    /// second launch happens, which is why the caller retries.
    /// </summary>
    private async Task<bool> TrySendAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            if (_useNamedPipe)
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    _endpoint,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                await pipe.ConnectAsync(200, cancellationToken).ConfigureAwait(false);

                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                await writer.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
                return true;
            }

            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), cancellationToken)
                .ConfigureAwait(false);

            using var stream = new NetworkStream(socket, ownsSocket: false);
            await using var streamWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            await streamWriter.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void DeleteStaleSocket()
    {
        if (_useNamedPipe)
            return;

        try
        {
            if (File.Exists(_endpoint))
                File.Delete(_endpoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the bind below reports a socket that really is in use
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Only the instance that bound the socket may remove it; a sender-side channel points
        // at the running instance's endpoint and must leave it alone.
        if (_useNamedPipe || !_listening)
            return;

        try
        {
            File.Delete(_endpoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the OS removes it on reboot; a leftover file is harmless
        }
    }
}
