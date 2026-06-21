import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr'
import { api, type Tick } from './api'

// Реалтайм-котировки: основной канал — SignalR push, фолбэк — клиентский polling.
export function useQuotes(connected: boolean) {
  const [quotes, setQuotes] = useState<Tick[]>([])
  const [live, setLive] = useState(false)
  const pollTimer = useRef<number | null>(null)

  useEffect(() => {
    if (!connected) {
      setQuotes([])
      setLive(false)
      return
    }

    let disposed = false
    let connection: HubConnection | null = null

    const startPolling = () => {
      if (pollTimer.current != null) return
      const tick = async () => {
        try {
          const data = await api.quotes()
          if (!disposed) setQuotes(data)
        } catch {
          /* игнорируем единичные ошибки опроса */
        }
      }
      void tick()
      pollTimer.current = window.setInterval(tick, 1500)
    }

    const stopPolling = () => {
      if (pollTimer.current != null) {
        window.clearInterval(pollTimer.current)
        pollTimer.current = null
      }
    }

    connection = new HubConnectionBuilder().withUrl('/hubs/marketdata').withAutomaticReconnect().build()

    connection.on('quotes', (payload: string) => {
      try {
        const data = JSON.parse(payload) as Tick[]
        if (!disposed) setQuotes(data)
      } catch {
        /* пропускаем некорректный кадр */
      }
    })

    connection.onreconnecting(() => setLive(false))
    connection.onreconnected(() => setLive(true))
    connection.onclose(() => {
      setLive(false)
      if (!disposed) startPolling()
    })

    connection
      .start()
      .then(() => {
        if (disposed) return
        setLive(true)
        stopPolling() // SignalR жив — polling не нужен
      })
      .catch(() => {
        if (!disposed) startPolling() // не удалось подключить hub — работаем по polling
      })

    return () => {
      disposed = true
      stopPolling()
      if (connection && connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [connected])

  return { quotes, live }
}
