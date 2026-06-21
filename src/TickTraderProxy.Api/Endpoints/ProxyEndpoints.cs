using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TickTraderProxy.Api.Hmac;
using TickTraderProxy.Api.Models;
using TickTraderProxy.Api.Services;

namespace TickTraderProxy.Api.Endpoints;

public static class ProxyEndpoints
{
    public static void MapProxyEndpoints(this IEndpointRouteBuilder app)
    {
        // Котировки по символам из конфига.
        app.MapGet("/api/quotes", (
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                IOptions<TickTraderOptions> options, ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, c => client.GetTicksAsync(string.Join(' ', options.Value.Symbols), c), ct));

        // Метаданные символов (нужны для precision → spread).
        app.MapGet("/api/symbols", (
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, client.GetSymbolsAsync, ct));

        // Level2 по символу с опциональной глубиной.
        app.MapGet("/api/level2/{symbol}", (
                string symbol, int? depth,
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, c => client.GetLevel2Async(symbol, depth, c), ct));

        // Счёт.
        app.MapGet("/api/account", (
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, client.GetAccountAsync, ct));

        // Открытые позиции/ордера.
        app.MapGet("/api/orders", (
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, client.GetTradesAsync, ct));

        // Создание рыночного ордера. Сервер собирает upstream-тело сам (Type=Market фиксирован).
        app.MapPost("/api/orders", (
                CreateOrderRequest body,
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Symbol) || string.IsNullOrWhiteSpace(body.Side) || body.Amount is null)
                return Task.FromResult(Results.BadRequest(new { error = "Symbol, Side и Amount обязательны." }));

            var upstream = JsonSerializer.Serialize(new
            {
                Type = "Market",
                Side = body.Side,
                Symbol = body.Symbol,
                Amount = body.Amount,
                Comment = body.Comment ?? string.Empty
            });
            return Proxy(ctx, store, loggerFactory, c => client.CreateOrderAsync(upstream, c), ct);
        });

        // Закрытие ордера (Type=Close на стороне клиента TickTrader).
        app.MapDelete("/api/orders/{id}", (
                string id, decimal? amount,
                HttpContext ctx, TickTraderClient client, SessionCredentialsStore store,
                ILoggerFactory loggerFactory, CancellationToken ct) =>
            Proxy(ctx, store, loggerFactory, c => client.CloseOrderAsync(id, amount, c), ct));
    }

    /// <summary>
    /// Общая обёртка: требует активную сессию, ставит амбиентные креды и ретранслирует
    /// статус + тело апстрима как есть. Деталей секрета/исключений наружу не отдаём.
    /// Неуспешные ответы апстрима логируем (только путь + статус, без секрета/тела).
    /// </summary>
    private static async Task<IResult> Proxy(
        HttpContext ctx,
        SessionCredentialsStore store,
        ILoggerFactory loggerFactory,
        Func<CancellationToken, Task<ProxyResult>> call,
        CancellationToken ct)
    {
        var credentials = store.Load(ctx.Session);
        if (credentials is null)
            return Results.Json(new { error = "Нет активной сессии. Сначала выполните connect." },
                statusCode: StatusCodes.Status401Unauthorized);

        ProxyResult result;
        using (AmbientCredentials.Use(credentials))
            result = await call(ct);

        if (!result.IsSuccess)
            loggerFactory.CreateLogger("TickTraderProxy.Proxy")
                .LogWarning("Прокси {Method} {Path} → TickTrader вернул {Status}",
                    ctx.Request.Method, ctx.Request.Path.Value, (int)result.StatusCode);

        return Results.Text(result.Content, result.ContentType, Encoding.UTF8, (int)result.StatusCode);
    }
}
