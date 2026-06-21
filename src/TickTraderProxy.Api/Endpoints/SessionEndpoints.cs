using Microsoft.Extensions.Options;
using TickTraderProxy.Api.Hmac;
using TickTraderProxy.Api.Models;
using TickTraderProxy.Api.Services;

namespace TickTraderProxy.Api.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/session");

        group.MapPost("/connect", async (
            ConnectRequest request,
            HttpContext context,
            TickTraderClient client,
            SessionCredentialsStore sessionStore,
            MarketFeedCredentialStore feed,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id)
                || string.IsNullOrWhiteSpace(request.Key)
                || string.IsNullOrWhiteSpace(request.Secret))
            {
                return Results.BadRequest(new { error = "Id, Key и Secret обязательны." });
            }

            var credentials = new HmacCredentials(request.Id, request.Key, request.Secret);

            // Валидируем креды живым вызовом /account под амбиентными кредами.
            ProxyResult account;
            using (AmbientCredentials.Use(credentials))
                account = await client.GetAccountAsync(ct);

            if (!account.IsSuccess)
            {
                // НЕ логируем секрет и тело ответа целиком — только факт и статус.
                logger.LogInformation("Connect отклонён: TickTrader /account вернул {Status}", (int)account.StatusCode);
                return Results.Json(new { error = "Не удалось авторизоваться в TickTrader. Проверьте Id/Key/Secret." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            sessionStore.Save(context.Session, credentials);
            feed.Set(credentials);
            await context.Session.CommitAsync(ct);

            return Results.Ok(new { connected = true });
        });

        group.MapPost("/disconnect", async (
            HttpContext context,
            SessionCredentialsStore sessionStore,
            MarketFeedCredentialStore feed,
            CancellationToken ct) =>
        {
            sessionStore.Clear(context.Session);
            feed.Clear();
            await context.Session.CommitAsync(ct);
            return Results.Ok(new { connected = false });
        });

        // Состояние сессии для фронта: есть ли активное подключение + интервал обновления
        // котировок из конфига (нужен клиентскому polling-фолбэку, чтобы не хардкодить).
        group.MapGet("/status", (
                HttpContext context, SessionCredentialsStore sessionStore, IOptions<TickTraderOptions> options) =>
            Results.Ok(new
            {
                connected = sessionStore.Load(context.Session) is not null,
                quoteRefreshIntervalMs = options.Value.QuoteRefreshIntervalMs
            }));
    }
}
