using TickTraderProxy.Api.Hmac;

namespace TickTraderProxy.Api.Services;

/// <summary>
/// Singleton-хранилище кредов для фонового опроса котировок (<c>QuoteBroadcastService</c>),
/// у которого нет HTTP-контекста/сессии. Заполняется при connect, чистится при disconnect.
///
/// Ограничение: один активный набор кредов на процесс — рассчитано на одного пользователя
/// (достаточно для тестового задания). Многопользовательский режим — см. README.
/// </summary>
public sealed class MarketFeedCredentialStore
{
    private volatile HmacCredentials? _credentials;

    public HmacCredentials? Current => _credentials;

    public void Set(HmacCredentials credentials) => _credentials = credentials;

    public void Clear() => _credentials = null;
}
