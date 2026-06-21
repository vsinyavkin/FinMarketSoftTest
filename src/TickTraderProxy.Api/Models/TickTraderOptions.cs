namespace TickTraderProxy.Api.Models;

/// <summary>
/// Конфигурация прокси, биндится из секции "TickTrader" в appsettings.json.
/// </summary>
public sealed class TickTraderOptions
{
    public const string SectionName = "TickTrader";

    /// <summary>Базовый адрес TickTrader Web API, напр. https://demonstrationwebapi.soft-fx.eu:8443</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Символы по умолчанию для котировок.</summary>
    public string[] Symbols { get; set; } = [];

    /// <summary>Интервал опроса котировок BackgroundService (мс).</summary>
    public int QuoteRefreshIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Послабление TLS — ТОЛЬКО для demo :8443 с самоподписанным сертификатом.
    /// Включать исключительно в Development. См. README.
    /// </summary>
    public bool AllowInvalidServerCertificate { get; set; }
}
