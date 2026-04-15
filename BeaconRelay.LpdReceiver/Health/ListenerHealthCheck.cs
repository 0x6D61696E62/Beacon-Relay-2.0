using BeaconRelay.LpdReceiver.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BeaconRelay.LpdReceiver.Health;

public sealed class ListenerHealthCheck(ListenerState listenerState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (listenerState.IsListening)
        {
            return Task.FromResult(HealthCheckResult.Healthy("LPD listener is accepting sessions."));
        }

        var data = new Dictionary<string, object>
        {
            ["lastError"] = listenerState.LastError ?? string.Empty,
            ["lastErrorUtc"] = listenerState.LastErrorUtc?.ToString("O") ?? string.Empty,
        };

        return Task.FromResult(HealthCheckResult.Unhealthy("LPD listener is not active.", data: data));
    }
}
