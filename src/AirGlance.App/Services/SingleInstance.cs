using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AirGlance.App.Services;

/// <summary>
/// Forwards the paths of a second launch to the instance that is already running, so
/// double-clicking another image reuses the open window instead of starting a second
/// process. The scope includes the settings directory, so an isolated test or benchmark
/// run can never forward into, or be hijacked by, a normal session.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexPrefix = "AirGlance.Instance.";
    private const string PipePrefix = "AirGlance.Forward.";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly Action<string[]> _onPaths;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;

    private SingleInstance(Mutex mutex, string pipeName, Action<string[]> onPaths)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _onPaths = onPaths;
    }

    /// <summary>
    /// Claims the name for this process. Returns null when another instance already owns
    /// it, which is the caller's signal to forward its arguments and exit.
    /// </summary>
    public static SingleInstance? Acquire(string scope, Action<string[]> onPaths)
    {
        ArgumentNullException.ThrowIfNull(onPaths);

        var key = ScopeKey(scope);
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexPrefix + key, out createdNew);
        }
        catch (Exception)
        {
            // A platform that cannot create the primitive is not worth failing over.
            return null;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var instance = new SingleInstance(mutex, PipePrefix + key, onPaths);
        instance.StartListening();
        return instance;
    }

    /// <summary>
    /// Sends paths to the instance that owns the scope. Returns false when nobody is
    /// listening, which happens if the owner is shutting down.
    /// </summary>
    public static bool Forward(string scope, IReadOnlyList<string> paths, int timeoutMilliseconds = 2000)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            using var client = new NamedPipeClientStream(".", PipePrefix + ScopeKey(scope), PipeDirection.Out);
            client.Connect(timeoutMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    writer.WriteLine(path);
                }
            }

            writer.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ScopeKey(string scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope ?? string.Empty));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private void StartListening() => _ = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        var token = _cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var paths = new List<string>();
                using var reader = new StreamReader(server, Encoding.UTF8);
                while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        paths.Add(line);
                    }
                }

                if (paths.Count > 0)
                {
                    _onPaths([.. paths]);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                // A dropped connection or a racing client must not end the listener.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; nothing to release.
        }

        _mutex.Dispose();
    }
}
