// Тонкий клиент к прокси. Фронт ходит ТОЛЬКО сюда — никогда напрямую в TickTrader.
// Секрет уходит лишь в connect() и больше нигде на клиенте не хранится.

export type Tick = {
  Symbol: string
  BestBid?: { Price: number; Volume: number }
  BestAsk?: { Price: number; Volume: number }
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
  status: () => request<{ connected: boolean }>('GET', '/api/session/status'),
  quotes: () => request<Tick[]>('GET', '/api/quotes'),
  symbols: () => request<SymbolInfo[]>('GET', '/api/symbols'),
  level2: (symbol: string, depth: number) =>
    request<unknown>('GET', `/api/level2/${encodeURIComponent(symbol)}?depth=${depth}`),
  account: () => request<Account>('GET', '/api/account'),
}
