import { useEffect, useMemo, useState } from 'react'
import {
  api,
  type Account,
  type Level2,
  type Level2Entry,
  type Order,
  type SymbolInfo,
  type Tick,
} from './api'
import { useQuotes } from './useQuotes'
import './App.css'

// --- Толерантные геттеры: имена полей выверяются по реальным образцам JSON TickTrader ---
function num(v: unknown): number | undefined {
  return typeof v === 'number' ? v : undefined
}
function bestPrice(side: unknown): number | undefined {
  if (side && typeof side === 'object' && 'Price' in side) return num((side as { Price: unknown }).Price)
  return num(side)
}
function getBid(t: Tick): number | undefined {
  return bestPrice(t.BestBid) ?? num(t.Bid)
}
function getAsk(t: Tick): number | undefined {
  return bestPrice(t.BestAsk) ?? num(t.Ask)
}
function getTime(t: Tick): string {
  // TickTrader отдаёт время тика как Unix ms; допускаем альтернативные имена полей.
  const ms = num(t.Timestamp) ?? num((t as Record<string, unknown>).Time)
  return ms != null ? new Date(ms).toLocaleTimeString() : '—'
}
function field(o: Record<string, unknown>, ...names: string[]): unknown {
  for (const n of names) if (o[n] != null) return o[n]
  return undefined
}

export default function App() {
  const [connected, setConnected] = useState(false)
  const [checking, setChecking] = useState(true)
  const [pollIntervalMs, setPollIntervalMs] = useState(1500)

  useEffect(() => {
    api
      .status()
      .then((s) => {
        setConnected(s.connected)
        if (typeof s.quoteRefreshIntervalMs === 'number') setPollIntervalMs(s.quoteRefreshIntervalMs)
      })
      .catch(() => setConnected(false))
      .finally(() => setChecking(false))
  }, [])

  if (checking) return <div className="app"><p>Загрузка…</p></div>

  return (
    <div className="app">
      <h1>TickTrader Web API — прокси</h1>
      {connected ? (
        <Dashboard pollIntervalMs={pollIntervalMs} onDisconnect={() => setConnected(false)} />
      ) : (
        <ConnectForm onConnected={() => setConnected(true)} />
      )}
    </div>
  )
}

function ConnectForm({ onConnected }: { onConnected: () => void }) {
  const [id, setId] = useState('')
  const [key, setKey] = useState('')
  const [secret, setSecret] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await api.connect(id.trim(), key.trim(), secret)
      setSecret('') // не держим секрет в состоянии дольше необходимого
      onConnected()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка подключения')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card" onSubmit={submit}>
      <h2>Подключение</h2>
      <label>Web API Id<input value={id} onChange={(e) => setId(e.target.value)} autoComplete="off" /></label>
      <label>Web API Key<input value={key} onChange={(e) => setKey(e.target.value)} autoComplete="off" /></label>
      <label>Web API Secret
        <input type="password" value={secret} onChange={(e) => setSecret(e.target.value)} autoComplete="off" />
      </label>
      {error && <p className="error">{error}</p>}
      <button disabled={busy || !id || !key || !secret}>{busy ? 'Подключаем…' : 'Подключиться'}</button>
    </form>
  )
}

function Dashboard({ pollIntervalMs, onDisconnect }: { pollIntervalMs: number; onDisconnect: () => void }) {
  const { quotes, live } = useQuotes(true, pollIntervalMs)
  const [symbols, setSymbols] = useState<SymbolInfo[]>([])

  useEffect(() => {
    api.symbols().then(setSymbols).catch(() => setSymbols([]))
  }, [])

  const precision = useMemo(() => {
    const map = new Map<string, number>()
    for (const s of symbols) if (typeof s.Precision === 'number') map.set(s.Symbol, s.Precision)
    return map
  }, [symbols])

  const symbolNames = useMemo(() => symbols.map((s) => s.Symbol), [symbols])

  const disconnect = async () => {
    try {
      await api.disconnect()
    } finally {
      onDisconnect() // в любом случае чистим UI
    }
  }

  return (
    <>
      <div className="toolbar">
        <span className={live ? 'badge live' : 'badge'}>{live ? '● SignalR' : '○ polling'}</span>
        <button onClick={disconnect}>Отключиться</button>
      </div>
      <AccountPanel />
      <QuotesTable quotes={quotes} precision={precision} />
      <Level2Panel symbols={symbolNames} />
      <OrdersPanel symbols={symbolNames} />
    </>
  )
}

function AccountPanel() {
  const [account, setAccount] = useState<Account | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = async () => {
    setError(null)
    try {
      setAccount(await api.account())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка')
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  const a = (account ?? {}) as Record<string, unknown>
  const rows: [string, unknown][] = [
    ['Счёт', field(a, 'Id', 'Account', 'AccountId')],
    ['Тип', field(a, 'AccountingType', 'Type')],
    ['Плечо', field(a, 'Leverage')],
    ['Баланс', field(a, 'Balance')],
    ['Валюта', field(a, 'BalanceCurrency', 'Currency')],
    ['Equity', field(a, 'Equity')],
    ['Margin level', field(a, 'MarginLevel')],
  ]

  return (
    <div className="card">
      <div className="card-head">
        <h2>Счёт</h2>
        <button onClick={refresh}>Refresh</button>
      </div>
      {error && <p className="error">{error}</p>}
      <table>
        <tbody>
          {rows.map(([label, value]) => (
            <tr key={label}>
              <td className="muted">{label}</td>
              <td>{value == null ? '—' : String(value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function QuotesTable({ quotes, precision }: { quotes: Tick[]; precision: Map<string, number> }) {
  return (
    <div className="card">
      <h2>Котировки</h2>
      <table className="quotes">
        <thead>
          <tr><th>Symbol</th><th>Time</th><th>Best Bid</th><th>Best Ask</th><th>Spread</th></tr>
        </thead>
        <tbody>
          {quotes.length === 0 && (
            <tr><td colSpan={5} className="muted">Ожидание данных…</td></tr>
          )}
          {quotes.map((t) => {
            const bid = getBid(t)
            const ask = getAsk(t)
            const p = precision.get(t.Symbol)
            // Spread = (BestAsk − BestBid) × 10^Precision; precision разный для символов
            const spread =
              bid != null && ask != null && p != null ? Math.round((ask - bid) * 10 ** p) : undefined
            return (
              <tr key={t.Symbol}>
                <td>{t.Symbol}</td>
                <td>{getTime(t)}</td>
                <td>{bid ?? '—'}</td>
                <td>{ask ?? '—'}</td>
                <td>{spread ?? '—'}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// --- Level 2 (стакан): выбор символа + глубины, Bid/Ask с объёмами ---
function entries(side: unknown): Level2Entry[] {
  return Array.isArray(side) ? (side as Level2Entry[]) : []
}

function Level2Panel({ symbols }: { symbols: string[] }) {
  const [symbol, setSymbol] = useState('')
  const [depth, setDepth] = useState(5)
  const [book, setBook] = useState<Level2 | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Выбрать первый доступный символ, когда подгрузился список.
  useEffect(() => {
    if (!symbol && symbols.length > 0) setSymbol(symbols[0])
  }, [symbols, symbol])

  useEffect(() => {
    if (!symbol) return
    let disposed = false
    const load = async () => {
      try {
        const data = await api.level2(symbol, depth)
        const one = Array.isArray(data) ? data[0] ?? null : data
        if (!disposed) {
          setBook(one)
          setError(null)
        }
      } catch (err) {
        if (!disposed) setError(err instanceof Error ? err.message : 'Ошибка')
      }
    }
    void load()
    const timer = window.setInterval(load, 2000) // периодический refetch стакана
    return () => {
      disposed = true
      window.clearInterval(timer)
    }
  }, [symbol, depth])

  const bids = entries(book?.Bids)
  const asks = entries(book?.Asks)
  const rows = Math.max(bids.length, asks.length)

  return (
    <div className="card">
      <div className="card-head">
        <h2>Level 2</h2>
        <div className="controls">
          <label>Символ
            <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
              {symbols.length === 0 && <option value="">—</option>}
              {symbols.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
          <label>Глубина
            <select value={depth} onChange={(e) => setDepth(Number(e.target.value))}>
              {[5, 10, 25, 50].map((d) => <option key={d} value={d}>{d}</option>)}
            </select>
          </label>
        </div>
      </div>
      {error && <p className="error">{error}</p>}
      <table className="quotes">
        <thead>
          <tr><th>Bid Volume</th><th>Bid</th><th>Ask</th><th>Ask Volume</th></tr>
        </thead>
        <tbody>
          {rows === 0 && <tr><td colSpan={4} className="muted">Нет данных стакана…</td></tr>}
          {Array.from({ length: rows }).map((_, i) => {
            const b = bids[i]
            const a = asks[i]
            return (
              <tr key={i}>
                <td>{b?.Volume ?? '—'}</td>
                <td>{b?.Price ?? '—'}</td>
                <td>{a?.Price ?? '—'}</td>
                <td>{a?.Volume ?? '—'}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// --- Ордера: таблица открытых + формы создания/закрытия + рефреш после операций ---
function OrdersPanel({ symbols }: { symbols: string[] }) {
  const [orders, setOrders] = useState<Order[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = async () => {
    setError(null)
    try {
      const data = await api.orders()
      setOrders(Array.isArray(data) ? data : [])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка')
    }
  }

  useEffect(() => {
    void refresh()
  }, [])

  return (
    <div className="card">
      <div className="card-head">
        <h2>Ордера</h2>
        <button onClick={refresh}>Refresh</button>
      </div>
      {error && <p className="error">{error}</p>}
      <table className="quotes">
        <thead>
          <tr><th>Id</th><th>Symbol</th><th>Side</th><th>Type</th><th>Price</th><th>Remaining</th></tr>
        </thead>
        <tbody>
          {orders.length === 0 && <tr><td colSpan={6} className="muted">Нет открытых ордеров</td></tr>}
          {orders.map((o, i) => {
            const r = o as Record<string, unknown>
            const id = field(r, 'Id', 'OrderId', 'TradeId')
            return (
              <tr key={String(id ?? i)}>
                <td>{id == null ? '—' : String(id)}</td>
                <td>{String(field(r, 'Symbol') ?? '—')}</td>
                <td>{String(field(r, 'Side') ?? '—')}</td>
                <td>{String(field(r, 'Type') ?? '—')}</td>
                <td>{String(field(r, 'Price') ?? '—')}</td>
                <td>{String(field(r, 'RemainingAmount', 'Amount') ?? '—')}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <div className="forms">
        <CreateOrderForm symbols={symbols} onDone={refresh} />
        <CloseOrderForm onDone={refresh} />
      </div>
    </div>
  )
}

function CreateOrderForm({ symbols, onDone }: { symbols: string[]; onDone: () => void }) {
  const [side, setSide] = useState('Buy')
  const [symbol, setSymbol] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!symbol && symbols.length > 0) setSymbol(symbols[0])
  }, [symbols, symbol])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    const amt = Number(amount)
    if (!symbol) return setError('Выберите символ')
    if (!Number.isFinite(amt) || amt <= 0) return setError('Amount должен быть положительным числом')
    setBusy(true)
    try {
      await api.createOrder({ Side: side, Symbol: symbol, Amount: amt })
      setAmount('')
      onDone()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка создания ордера')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="subcard" onSubmit={submit}>
      <h3>Новый маркет-ордер</h3>
      <label>Type<input value="Market" disabled /></label>
      <label>Side
        <select value={side} onChange={(e) => setSide(e.target.value)}>
          <option value="Buy">Buy</option>
          <option value="Sell">Sell</option>
        </select>
      </label>
      <label>Symbol
        <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
          {symbols.length === 0 && <option value="">—</option>}
          {symbols.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </label>
      <label>Amount<input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" /></label>
      {error && <p className="error">{error}</p>}
      <button disabled={busy}>{busy ? 'Отправка…' : 'Создать ордер'}</button>
    </form>
  )
}

function CloseOrderForm({ onDone }: { onDone: () => void }) {
  const [id, setId] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    if (!id.trim()) return setError('Укажите Order Id')
    const amt = amount.trim() === '' ? undefined : Number(amount)
    if (amt != null && (!Number.isFinite(amt) || amt <= 0)) return setError('Amount должен быть положительным')
    setBusy(true)
    try {
      await api.closeOrder(id.trim(), amt)
      setId('')
      setAmount('')
      onDone()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка закрытия ордера')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="subcard" onSubmit={submit}>
      <h3>Закрыть ордер</h3>
      <label>Order Id<input value={id} onChange={(e) => setId(e.target.value)} /></label>
      <label>Type<input value="Close" disabled /></label>
      <label>Amount (опц.)<input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" /></label>
      {error && <p className="error">{error}</p>}
      <button disabled={busy}>{busy ? 'Отправка…' : 'Закрыть ордер'}</button>
    </form>
  )
}
