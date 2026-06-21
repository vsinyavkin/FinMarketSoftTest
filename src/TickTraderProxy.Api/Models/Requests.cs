namespace TickTraderProxy.Api.Models;

/// <summary>Тело POST /api/session/connect. Secret уходит только сюда и только на сервер.</summary>
public sealed record ConnectRequest(string? Id, string? Key, string? Secret);

/// <summary>Тело POST /api/orders — создание рыночного ордера (каркас, шаг 7).</summary>
public sealed record CreateOrderRequest(string? Symbol, string? Side, decimal? Amount, string? Comment);
