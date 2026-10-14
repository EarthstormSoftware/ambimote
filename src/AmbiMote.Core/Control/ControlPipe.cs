using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace AmbiMote.Core.Control;

/// <summary>
/// The local named pipe the running app listens on, so the <c>ambimote</c> command, scripts and other tools can control
/// it. Each request is one line (see <see cref="ControlCommand"/>) and each answer one line of JSON (see
/// <see cref="ControlReply"/>). Only the signed-in user can connect, and nothing leaves the PC.
/// </summary>
public static class ControlPipe
{
    /// <summary>Held by the running app, so only one copy drives the sticks in each Windows session.</summary>
    public const string RunningMutexName = @"Local\AmbiMote.Running";

    /// <summary>The longest request or reply, in bytes. Requests are a few words; replies a few hundred bytes.</summary>
    internal const int MaxLineBytes = 4096;

    /// <summary>The pipe name for this Windows session, so users signed in at the same time each control their own app.</summary>
    public static string DefaultName { get; } = $"AmbiMote.Control.{Process.GetCurrentProcess().SessionId}";

    /// <summary>Whether the app is running in this Windows session.</summary>
    public static bool IsAppRunning()
    {
        if (Mutex.TryOpenExisting(RunningMutexName, out var mutex))
        {
            mutex.Dispose();
            return true;
        }

        return false;
    }

    /// <summary>Sends one request and waits for the reply.</summary>
    /// <exception cref="TimeoutException">Nothing answered within <paramref name="timeout"/>.</exception>
    /// <exception cref="IOException">The connection failed or the reply was too long.</exception>
    public static async Task<string> SendAsync(string pipeName, string request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token);
            await WriteLineAsync(pipe, request, deadline.Token);
            return await ReadLineAsync(pipe, deadline.Token) ?? throw new IOException("The app closed the connection without answering.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The AmbiMote app did not answer.");
        }
    }

    internal static async Task WriteLineAsync(Stream stream, string line, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line.ReplaceLineEndings(" ") + "\n");
        if (bytes.Length > MaxLineBytes)
        {
            throw new IOException("The message is too long.");
        }

        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>Reads up to the next newline, or null if the other end closed first.</summary>
    internal static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxLineBytes];
        int length = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            if (buffer[length] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\r');
            }

            if (++length == buffer.Length)
            {
                throw new IOException("The message is too long.");
            }
        }
    }
}

/// <summary>
/// Answers requests on a <see cref="ControlPipe"/>, one connection at a time, until disposed. Requests are short, so a
/// slow or stuck client is dropped after a few seconds rather than holding up the next.
/// </summary>
public sealed class ControlServer : IAsyncDisposable
{
    private static readonly TimeSpan s_connectionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly Func<string, Task<string>> _handler;
    private readonly CancellationTokenSource _stop = new();
    private Task? _running;

    /// <param name="pipeName">Usually <see cref="ControlPipe.DefaultName"/>.</param>
    /// <param name="handler">Turns a request line into a reply line. Called on a thread-pool thread.</param>
    public ControlServer(string pipeName, Func<string, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        ArgumentNullException.ThrowIfNull(handler);
        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start() => _running ??= Task.Run(() => RunAsync(_stop.Token));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_running is not null)
        {
            await _running.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another program holds the name. Remote control is a convenience, so keep trying quietly.
                if (!await DelayAsync(s_retryDelay, cancellationToken))
                {
                    return;
                }

                continue;
            }

            await using (pipe)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(s_connectionTimeout);
                    if (await ControlPipe.ReadLineAsync(pipe, deadline.Token) is { } request)
                    {
                        string reply;
                        try
                        {
                            reply = await _handler(request).WaitAsync(deadline.Token);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            reply = ControlReply.Failed($"The app could not do that: {ex.Message}").ToJson();
                        }

                        await ControlPipe.WriteLineAsync(pipe, reply, deadline.Token);
                        pipe.WaitForPipeDrain();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
                {
                    // The client went away or took too long; wait for the next one.
                }
            }
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
