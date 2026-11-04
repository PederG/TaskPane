using System.IO.Pipes;
using System.Text;

namespace TaskPane.App.Services;

/// <summary>Ensures a single app instance per user session and signals an existing instance to activate.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\TaskPane.SingleInstance";
    private const string PipeName = "TaskPane.Activate";

    private readonly Mutex _mutex;
    private readonly bool _createdNew;
    private CancellationTokenSource? _listenCts;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out _createdNew);
    }

    public bool IsPrimaryInstance => _createdNew;

    public event Action? ActivationRequested;

    public void StartListening()
    {
        if (!_createdNew)
        {
            return;
        }

        _listenCts = new CancellationTokenSource();
        _ = ListenAsync(_listenCts.Token);
    }

    public static async Task<bool> SignalExistingInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            await client.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes("activate");
            await client.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[64];
                _ = await server.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        if (_createdNew)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // ignored
            }
        }

        _mutex.Dispose();
    }
}
