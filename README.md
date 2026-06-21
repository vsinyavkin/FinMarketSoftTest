# TickTrader Web API Proxy

Веб-приложение: ASP.NET Core backend-прокси + React/TS фронт. Через серверный прокси
с HMAC-подписью ходит в TickTrader Web API и показывает котировки (Bid/Ask/Spread/Time),
стакан Level 2, информацию о счёте и работу с ордерами (создание/закрытие).
Тестовое задание для Finmarket Soft.

Инвариант: **Браузер → прокси (ASP.NET) → HMAC на сервере → TickTrader.**
Фронт никогда не ходит в TickTrader напрямую и не видит секрет.

## Стек
- Backend: ASP.NET Core, Minimal API, целевой фреймворк `net8.0` (LTS)
- HTTP: `IHttpClientFactory` + типизированный `TickTraderClient` + `DelegatingHandler` для HMAC
- Креды: серверная сессия + `IDataProtector` для секрета (на клиент секрет не уходит)
- Реалтайм: SignalR + `BackgroundService` (опрос по интервалу из конфига); фолбэк — клиентский polling
- Frontend: React + TypeScript (Vite), `@microsoft/signalr`
- Тесты: xUnit (тест HMAC-подписи)

## Требования
- .NET SDK 8 или 9 (проект таргетит `net8.0`; собран и проверен на SDK 9)
- Node.js 18+ и npm

> **Про .NET 8 vs 9.** ТЗ фиксирует .NET 8 (LTS), поэтому проекты таргетят `net8.0`.
> На машине разработки стоял только runtime .NET 9, поэтому добавлен
> `<RollForward>LatestMajor</RollForward>` — сборка идёт SDK 9, запуск на runtime .NET 9.
> Если есть runtime .NET 8 — приложение запустится на нём без изменений.

## Структура
```
src/TickTraderProxy.Api    backend (Hmac/ Services/ Models/ Hubs/ Endpoints/)
tests/TickTraderProxy.Tests xUnit (тест HMAC)
client/                    React+TS (Vite); билд → src/TickTraderProxy.Api/wwwroot
```

## Сборка и запуск

### 1. Фронт → wwwroot
```bash
cd client
npm install
npm run build      # кладёт статику в ../src/TickTraderProxy.Api/wwwroot
```

### 2. Backend
```bash
cd src/TickTraderProxy.Api
dotnet run         # http://localhost:5080
```
Открыть http://localhost:5080 — фронт отдаётся как статика из `wwwroot`.

### Режим разработки с hot-reload фронта
Два процесса параллельно:
```bash
# терминал 1 — backend
cd src/TickTraderProxy.Api && dotnet run

# терминал 2 — Vite dev-server (проксирует /api и /hubs на :5080)
cd client && npm run dev      # http://localhost:5173
```

### Тесты
```bash
dotnet test
```

## Конфигурация (`appsettings.json` → `TickTrader`)
| Ключ | Назначение |
|------|------------|
| `BaseUrl` | адрес TickTrader Web API (`https://demonstrationwebapi.soft-fx.eu:8443`) |
| `Symbols` | символы котировок по умолчанию (`BTCUSD, EURUSD, GBPUSD., AUDUSD., USDCHF., USDJPY.`) |
| `QuoteRefreshIntervalMs` | интервал фонового опроса котировок |
| `AllowInvalidServerCertificate` | послабление TLS — см. ниже |

## HMAC-аутентификация
```
signatureString = unixTimestampMs + webApiId + webApiKey + httpMethod + requestUri + body
base64          = Base64( HMACSHA256( key = webApiSecret, message = signatureString ) )
Header: Authorization: HMAC {webApiId}:{webApiKey}:{unixTimestampMs}:{base64}
```
- Кодировка байт — **US-ASCII** (message и key), как в эталонной реализации SoftFX
  (секция Q&A на `…:8443/api/doc/index`). Реализация и пиннинг — `src/.../Hmac/HmacSigner.cs`,
  тест `tests/.../HmacSignerTests.cs`. **Подтверждено живым вызовом к demo-API** (см. ниже).
- Подписывается **ровно тот** абсолютный URI и тело, что уходят в запрос
  (без нормализации; `{filter}` url-encoded, пробел → `%20`).
- `body`: GET/DELETE → `""`; POST → ровно JSON тела.
- Секрет никогда не логируется, не возвращается на клиент и не попадает в URL/query.

## Эндпоинты прокси
- `POST /api/session/connect {id,key,secret}` — валидирует через `/account`, кладёт в сессию
- `POST /api/session/disconnect` — чистит сессию
- `GET  /api/session/status` — есть ли активное подключение
- `GET  /api/quotes`, `GET /api/symbols`, `GET /api/level2/{symbol}?depth=`
- `GET  /api/account`, `GET /api/orders`
- `POST /api/orders` (маркет-ордер), `DELETE /api/orders/{id}?amount=` (закрытие)
- `GET  /api/session/status` отдаёт `quoteRefreshIntervalMs` — клиентский polling-фолбэк
  берёт интервал из конфига, а не хардкодит
- SignalR hub: `/hubs/marketdata` (сообщение `quotes` — сырой JSON `/tick`)

## TickTrader Web API (вызовы за прокси)
Фронт сюда не ходит — это апстрим, который дёргает прокси после HMAC-подписи.
- `GET /api/v2/tick/{filter}` — котировки
- `GET /api/v2/symbol/{filter}` | `GET /api/v2/symbol` — метаданные символов (precision)
- `GET /api/v2/level2/{filter}?depth=N` — стакан Level 2
- `GET /api/v2/account` — счёт
- `GET /api/v2/trade` — открытые ордера
- `POST /api/v2/trade` body `{Type:"Market",Side,Symbol,Amount,Comment}` — маркет-ордер
- `DELETE /api/v2/trade?Type=Close&Id={id}&Amount=N` — закрытие
- `{filter}` = имя символа или список через пробел, url-encoded

## Бизнес-правила UI
- **Котировки**: колонки Symbol / Time / Best Bid / Best Ask / Spread.
  `Spread = (BestAsk − BestBid) × 10^Precision`, где `Precision` берётся из метаданных
  символа (`/symbols`) и различается между символами. Автообновление — SignalR push;
  фолбэк — клиентский polling по `quoteRefreshIntervalMs` из `/api/session/status`.
- **Level 2**: выбор символа + выбор глубины; Bid/Ask с объёмами, периодический refetch.
- **Account**: номер, тип, плечо, баланс + валюта, equity, margin level, кнопка Refresh.
  `margin level` вычисляется как `Equity / Margin × 100%` (апи не отдаёт готовое поле);
  при `Margin = 0` (нет открытых позиций) — «—».
- **Ордера**: таблица открытых (Id / Symbol / Side / Type / Price / Remaining), форма
  создания маркет-ордера, форма закрытия; список рефрешится после операций.
- **Disconnect**: чистит чувствительные данные и останавливает обновление UI.

## Послабление TLS (временное, только demo)
Demo на `:8443` может отдавать самоподписанный сертификат. Для этого предусмотрен флаг
`TickTrader:AllowInvalidServerCertificate`. Он включён **только** в
`appsettings.Development.json` и в проде должен быть `false`. Это временная мера для demo.

## Живой прогон к demo-API (выполнен)
Проверено сквозным прогоном через прокси с реальными Web API Id/Key/Secret demo-среды:
- **HMAC** принята живым API — `connect` валидирует креды через `/account`.
- **Счёт / котировки / Level 2 / ордера** — имена полей в реальном JSON совпали с
  толерантными геттерами фронта (`Id`, `AccountingType`, `Leverage`, `Balance`,
  `BalanceCurrency`, `Equity`; `Symbol`, `Timestamp`, `BestBid/BestAsk.Price`;
  `Precision`; `Bids/Asks` с `Price/Volume`; `Id/Type/Side/Symbol/Price/RemainingAmount`).
- **Ордера** — полный цикл: создание market-ордера → виден в списке → закрытие → список пуст.
- **Сессия** — disconnect чистит креды (после него `/api/account` → 401).
- Поправка по итогам прогона: `margin level` считается на клиенте (`Equity/Margin`), т.к.
  апи не отдаёт готовое поле `MarginLevel`.

## Что не успел / ограничения
- **Реалтайм для Level 2 и ордеров** — обновление по периодическому refetch на клиенте
  (стакан — каждые 2 с; ордера — по кнопке Refresh и после операций). Push через SignalR
  сделан только для котировок.
- **Многопользовательность**: фоновый опрос котировок использует один набор кредов на
  процесс (`MarketFeedCredentialStore`) — рассчитано на одного пользователя, чего
  достаточно для тестового задания.
- **БД нет** по ТЗ — только серверная сессия (in-memory distributed cache).
