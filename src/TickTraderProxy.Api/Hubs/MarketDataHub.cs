using Microsoft.AspNetCore.SignalR;

namespace TickTraderProxy.Api.Hubs;

/// <summary>
/// Hub реалтайм-котировок. Сервер пушит сообщение "quotes" (сырой JSON от TickTrader /tick).
/// Клиент только подписывается — методов для вызова с клиента нет.
/// </summary>
public sealed class MarketDataHub : Hub
{
}
