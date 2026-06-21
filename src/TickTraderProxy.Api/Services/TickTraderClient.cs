using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TickTraderProxy.Api.Models;

namespace TickTraderProxy.Api.Services;

/// <summary>Результат проксируемого вызова: статус + сырое тело (как правило JSON).</summary>
public sealed record ProxyResult(HttpStatusCode StatusCode, string Content, string ContentType = "application/json")
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;
}

/// <summary>
/// Типизированный клиент TickTrader Web API. Строит АБСОЛЮТНЫЕ URI из конфигурируемого BaseUrl,
/// чтобы подписываемая строка совпадала с отправляемой. Подпись добавляет <c>HmacAuthHandler</c>.
/// Креды должны быть установлены через <c>AmbientCredentials.Use(...)</c> вокруг вызова.
/// </summary>
public sealed class TickTraderClient(HttpClient http, IOptions<TickTraderOptions> options)
{
    private readonly string _baseUrl = options.Value.BaseUrl.TrimEnd('/');

    public Task<ProxyResult> GetAccountAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "/api/v2/account", null, ct);

    public Task<ProxyResult> GetTicksAsync(string filter, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"/api/v2/tick/{Encode(filter)}", null, ct);

    public Task<ProxyResult> GetSymbolsAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "/api/v2/symbol", null, ct);

    public Task<ProxyResult> GetLevel2Async(string symbol, int? depth, CancellationToken ct)
    {
        var path = $"/api/v2/level2/{Encode(symbol)}";
        if (depth is { } d) path += $"?depth={d.ToString(CultureInfo.InvariantCulture)}";
        return SendAsync(HttpMethod.Get, path, null, ct);
    }

    public Task<ProxyResult> GetTradesAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Get, "/api/v2/trade", null, ct);

    // --- Ордера: тонкий проброс к /api/v2/trade (создание/закрытие). ---
    public Task<ProxyResult> CreateOrderAsync(string json, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "/api/v2/trade", json, ct);

    public Task<ProxyResult> CloseOrderAsync(string id, decimal? amount, CancellationToken ct)
    {
        var path = $"/api/v2/trade?Type=Close&Id={Uri.EscapeDataString(id)}";
        if (amount is { } a) path += $"&Amount={a.ToString(CultureInfo.InvariantCulture)}";
        return SendAsync(HttpMethod.Delete, path, null, ct);
    }

    /// <summary>{filter} = символ или список через пробел; пробел кодируется как %20.</summary>
    private static string Encode(string filter) => Uri.EscapeDataString(filter);

    private async Task<ProxyResult> SendAsync(
        HttpMethod method, string pathAndQuery, string? jsonBody, CancellationToken ct)
    {
        var uri = _baseUrl + pathAndQuery;
        using var request = new HttpRequestMessage(method, uri);
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return new ProxyResult(response.StatusCode, content);
    }
}
