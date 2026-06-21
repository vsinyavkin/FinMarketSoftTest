namespace TickTraderProxy.Api.Hmac;

/// <summary>
/// DelegatingHandler, который подписывает каждый исходящий запрос к TickTrader Web API.
/// Креды берутся из <see cref="AmbientCredentials"/> (см. там — почему AsyncLocal, а не DI).
/// Подписывается ровно тот абсолютный URI и тело, что уходят в запрос.
/// </summary>
public sealed class HmacAuthHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var credentials = AmbientCredentials.Current
            ?? throw new InvalidOperationException(
                "HMAC-креды не установлены для текущего вызова (нет AmbientCredentials.Use).");

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var method = request.Method.Method;
        // AbsoluteUri — ровно то, что HttpClient положит в строку запроса (без доп. нормализации).
        var requestUri = request.RequestUri!.AbsoluteUri;
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var header = HmacSigner.BuildAuthorizationHeader(credentials, timestampMs, method, requestUri, body);
        request.Headers.TryAddWithoutValidation("Authorization", header);

        return await base.SendAsync(request, cancellationToken);
    }
}
