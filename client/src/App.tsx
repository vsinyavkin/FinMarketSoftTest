import { useEffect, useMemo, useState } from 'react'
import { api, type Account, type SymbolInfo, type Tick } from './api'
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
function field(o: Record<string, unknown>, ...names: string[]): unknown {
  for (const n of names) if (o[n] != null) return o[n]
  return undefined
}

export default function App() {
  const [connected, setConnected] = useState(false)
  const [checking, setChecking] = useState(true)

  useEffect(() => {
    api
      .status()
      .then((s) => setConnected(s.connected))
      .catch(() => setConnected(false))
      .finally(() => setChecking(false))
  }, [])

  if (checking) return <div className="app"><p>Загрузка…</p></div>

  return (
    <div className="app">
      <h1>TickTrader Web API — прокси</h1>
      {connected ? (
        <Dashboard onDisconnect={() => setConnected(false)} />
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

function Dashboard({ onDisconnect }: { onDisconnect: () => void }) {
  const { quotes, live } = useQuotes(true)
  const [symbols, setSymbols] = useState<SymbolInfo[]>([])

  useEffect(() => {
    api.symbols().then(setSymbols).catch(() => setSymbols([]))
  }, [])

  const precision = useMemo(() => {
    const map = new Map<string, number>()
    for (const s of symbols) if (typeof s.Precision === 'number') map.set(s.Symbol, s.Precision)
    return map
  }, [symbols])

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
          <tr><th>Символ</th><th>Bid</th><th>Ask</th><th>Spread</th></tr>
        </thead>
        <tbody>
          {quotes.length === 0 && (
            <tr><td colSpan={4} className="muted">Ожидание данных…</td></tr>
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
