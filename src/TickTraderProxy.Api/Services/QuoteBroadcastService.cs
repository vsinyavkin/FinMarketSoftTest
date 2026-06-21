using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TickTraderProxy.Api.Hmac;
using TickTraderProxy.Api.Hubs;
using TickTraderProxy.Api.Models;

namespace TickTraderProxy.Api.Services;

/// <summary>
/// Фоновый опрос котировок по интервалу из конфига и push в SignalR-хаб.
/// Использует креды из <see cref="MarketFeedCredentialStore"/>; пока никто не подключён — простаивает.
/// </summary>
public sealed class QuoteBroadcastService(
    IServiceScopeFactory scopeFactory,
    IHubContext<MarketDataHub> hub,
    MarketFeedCredentialStore feed,
    IOptions<TickTraderOptions> options,
    ILogger<QuoteBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMs = Math.Max(200, options.Value.QuoteRefreshIntervalMs);
        var filter = string.Join(' ', options.Value.Symbols);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var credentials = feed.Current;
            if (credentials is null)
                continue; // нет активной сессии — ничего не опрашиваем

            try
            {
                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<TickTraderClient>();

                ProxyResult result;
                using (AmbientCredentials.Use(credentials))
                    result = await client.GetTicksAsync(filter, stoppingToken);

                if (result.IsSuccess)
                    await hub.Clients.All.SendAsync("quotes", result.Content, stoppingToken);
                else
                    logger.LogWarning("Опрос котировок: TickTrader вернул {Status}", (int)result.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось опросить котировки");
            }
        }
    }
}
