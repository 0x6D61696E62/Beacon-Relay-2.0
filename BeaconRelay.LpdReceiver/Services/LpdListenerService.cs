using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BeaconRelay.LpdReceiver.Options;
using BeaconRelay.LpdReceiver.Protocol;
using Microsoft.Extensions.Options;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class LpdListenerService(
    IServiceScopeFactory scopeFactory,
    IOptions<LpdOptions> options,
    ListenerState listenerState,
    ILogger<LpdListenerService> logger) : BackgroundService
{
    private readonly LpdOptions _options = options.Value;
    private readonly ConcurrentDictionary<int, Task> _activeSessions = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoffSeconds = Math.Max(1, _options.InitialRestartBackoffSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            TcpListener? listener = null;

            try
            {
                listener = new TcpListener(IPAddress.Any, _options.Port);
                listener.Start();
                listenerState.MarkListening(true);
                backoffSeconds = Math.Max(1, _options.InitialRestartBackoffSeconds);

                logger.LogInformation("LPD listener started on port {Port}", _options.Port);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stoppingToken);
                    client.NoDelay = true;
                    client.ReceiveTimeout = _options.SessionTimeoutSeconds * 1000;
                    client.SendTimeout = _options.SessionTimeoutSeconds * 1000;

                    var sessionTask = HandleClientAsync(client, stoppingToken);
                    _activeSessions.TryAdd(sessionTask.Id, sessionTask);
                    _ = sessionTask.ContinueWith(
                        t => _activeSessions.TryRemove(t.Id, out _),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                listenerState.MarkError(ex);
                logger.LogError(ex, "LPD listener failure. Restarting in {BackoffSeconds}s", backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken);
                backoffSeconds = Math.Min(backoffSeconds * 2, Math.Max(1, _options.MaxRestartBackoffSeconds));
            }
            finally
            {
                listenerState.MarkListening(false);
                listener?.Stop();
            }
        }

        await Task.WhenAll(_activeSessions.Values);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            var remote = (IPEndPoint?)client.Client.RemoteEndPoint;
            var remoteHost = remote?.Address.ToString() ?? "unknown";
            var remotePort = remote?.Port ?? 0;

            using var scope = scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<LpdSessionHandler>();
            var processor = scope.ServiceProvider.GetRequiredService<ReceivedFileProcessor>();

            var result = await handler.HandleAsync(client, cancellationToken);
            if (result.Success && result.Job is not null)
            {
                await processor.PersistSuccessfulSessionAsync(result.Job, cancellationToken);
                return;
            }

            var error = result.Error ?? "Unknown session error.";
            logger.LogWarning(
                "LPD session rejected for {RemoteHost}:{RemotePort}, queue={QueueName}, reason={Reason}",
                remoteHost,
                remotePort,
                result.QueueName,
                error);

            await processor.PersistFailedSessionAsync(remoteHost, remotePort, result.QueueName, error, cancellationToken);
        }
    }
}
