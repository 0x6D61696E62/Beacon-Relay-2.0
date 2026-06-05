using System.Net;
using System.Net.Mail;
using BeaconRelay.LpdReceiver.Data;
using Microsoft.EntityFrameworkCore;

namespace BeaconRelay.LpdReceiver.Services;

public sealed class ListenerAlertService(
    IServiceScopeFactory scopeFactory,
    ListenerState listenerState,
    IHttpClientFactory httpClientFactory,
    ILogger<ListenerAlertService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private DateTime _nextMonitorUtc = DateTime.MinValue;
    private DateTime _nextDownEmailUtc = DateTime.MinValue;
    private bool _downEmailSent;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await LoadSettingsAsync(stoppingToken);
                await ProcessAlertsAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Listener alert loop failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task<AlertSettingsRecord> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.AlertSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, cancellationToken);

        return settings ?? new AlertSettingsRecord
        {
            Id = 1,
            MonitorIntervalSeconds = 60,
            EmailSmtpPort = 587,
            EmailUseSsl = true,
            ListenerDownEmailCooldownMinutes = 30,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private async Task ProcessAlertsAsync(AlertSettingsRecord settings, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var isListening = listenerState.IsListening;

        if (isListening)
        {
            _downEmailSent = false;
            _nextDownEmailUtc = DateTime.MinValue;

            if (settings.MonitorEnabled
                && !string.IsNullOrWhiteSpace(settings.MonitorUrl)
                && nowUtc >= _nextMonitorUtc)
            {
                await SendMonitorMessageAsync(settings.MonitorUrl.Trim(), cancellationToken);
                _nextMonitorUtc = nowUtc.AddSeconds(Math.Max(10, settings.MonitorIntervalSeconds));
            }

            return;
        }

        if (settings.EmailEnabled
            && !string.IsNullOrWhiteSpace(settings.EmailSmtpHost)
            && !string.IsNullOrWhiteSpace(settings.EmailFrom)
            && !string.IsNullOrWhiteSpace(settings.EmailTo)
            && nowUtc >= _nextDownEmailUtc
            && !_downEmailSent)
        {
            var sent = await SendListenerDownEmailAsync(settings, cancellationToken);
            if (sent)
            {
                _downEmailSent = true;
                _nextDownEmailUtc = nowUtc.AddMinutes(Math.Max(1, settings.ListenerDownEmailCooldownMinutes));
            }
        }
    }

    private async Task SendMonitorMessageAsync(string monitorUrl, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, monitorUrl)
            {
                Content = new StringContent("{\"status\":\"listening\",\"timestampUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}", System.Text.Encoding.UTF8, "application/json")
            };

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CRON monitor message returned non-success status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed sending CRON monitor message.");
        }
    }

    private async Task<bool> SendListenerDownEmailAsync(AlertSettingsRecord settings, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(settings.EmailFrom!.Trim());
            foreach (var recipient in settings.EmailTo!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                message.To.Add(recipient);
            }

            message.Subject = "Beacon Relay Alert: LPD listener is not listening";
            message.Body =
                "The LPD listener is currently not listening." + Environment.NewLine
                + "Timestamp (UTC): " + DateTime.UtcNow.ToString("O") + Environment.NewLine
                + "Last error: " + (listenerState.LastError ?? "(none)") + Environment.NewLine
                + "Last error UTC: " + (listenerState.LastErrorUtc?.ToString("O") ?? "(none)");

            using var client = new SmtpClient(settings.EmailSmtpHost!.Trim(), settings.EmailSmtpPort)
            {
                EnableSsl = settings.EmailUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(settings.EmailUsername))
            {
                client.Credentials = new NetworkCredential(settings.EmailUsername, settings.EmailPassword ?? string.Empty);
            }

            await client.SendMailAsync(message, cancellationToken);
            logger.LogInformation("LPD listener-down email alert sent to {Recipients}", settings.EmailTo);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed sending LPD listener-down email alert.");
            return false;
        }
    }
}
