namespace TickTraderProxy.Api.Hmac;

/// <summary>
/// Амбиентные (AsyncLocal) HMAC-креды для текущего логического вызова.
///
/// Зачем: <see cref="HmacAuthHandler"/> — это пулящийся DelegatingHandler, в который нельзя
/// безопасно инжектить scoped-сервисы (классическая ловушка времён жизни HttpClient).
/// AsyncLocal протекает по await-цепочке вызывающего кода внутрь конвейера HttpClient,
/// поэтому одинаково работает для трёх источников кредов: connect (из тела запроса),
/// проксируемые запросы (из сессии) и BackgroundService (из singleton-стора).
///
/// Использование:
///   using (AmbientCredentials.Use(creds))
///       await tickTraderClient.GetAccountAsync(ct);
/// </summary>
public static class AmbientCredentials
{
    private static readonly AsyncLocal<HmacCredentials?> Slot = new();

    public static HmacCredentials? Current => Slot.Value;

    public static IDisposable Use(HmacCredentials credentials)
    {
        var previous = Slot.Value;
        Slot.Value = credentials;
        return new Restore(previous);
    }

    private sealed class Restore(HmacCredentials? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Slot.Value = previous;
        }
    }
}
