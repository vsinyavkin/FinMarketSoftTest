// Тонкий клиент к прокси. Фронт ходит ТОЛЬКО сюда — никогда напрямую в TickTrader.
// Секрет уходит лишь в connect() и больше нигде на клиенте не хранится.

export type Tick = {
  Symbol: string
  BestBid?: { Price: number; Volume: number }
  BestAsk?: { Price: number; Volume: number }
  // время котировки (TickTrader отдаёт Unix ms); допускаем альтернативные имена
  Timestamp?: number
  // допускаем альтернативные имена полей до выверки по реальным образцам JSON
  [key: string]: unknown
}

export type SymbolInfo = {
  Symbol: string
  Precision?: number
  [key: string]: unknown
}

export type Account = {
  [key: string]: unknown
}

// Запись стакана Level2: цена + объём. Имена полей выверяются по реальному JSON.
export type Level2Entry = {
  Price?: number
  Volume?: number
  [key: string]: unknown
}

export type Level2 = {
  Symbol?: string
  Bids?: Level2Entry[]
  Asks?: Level2Entry[]
  [key: string]: unknown
}

export type Order = {
  [key: string]: unknown
}

export type SessionStatus = { connected: boolean; quoteRefreshIntervalMs?: number }

export type CreateOrderInput = { Side: string; Symbol: string; Amount: number; Comment?: string }

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  const data = text ? JSON.parse(text) : null
  if (!res.ok) {
    const message = (data && (data.error as string)) || `HTTP ${res.status}`
    throw new Error(message)
  }
  return data as T
}

export const api = {
  connect: (id: string, key: string, secret: string) =>
    request<{ connected: boolean }>('POST', '/api/session/connect', { id, key, secret }),
  disconnect: () => request<{ connected: boolean }>('POST', '/api/session/disconnect'),
  status: () => request<SessionStatus>('GET', '/api/session/status'),
  quotes: () => request<Tick[]>('GET', '/api/quotes'),
  symbols: () => request<SymbolInfo[]>('GET', '/api/symbols'),
  level2: (symbol: string, depth: number) =>
    request<Level2 | Level2[]>('GET', `/api/level2/${encodeURIComponent(symbol)}?depth=${depth}`),
  account: () => request<Account>('GET', '/api/account'),
  orders: () => request<Order[]>('GET', '/api/orders'),
  createOrder: (input: CreateOrderInput) => request<unknown>('POST', '/api/orders', input),
  closeOrder: (id: string, amount?: number) =>
    request<unknown>(
      'DELETE',
      `/api/orders/${encodeURIComponent(id)}${amount != null ? `?amount=${amount}` : ''}`,
    ),
}
