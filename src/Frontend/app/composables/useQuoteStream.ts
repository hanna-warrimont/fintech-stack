export interface Tick {
  instrumentId: string
  seq: number
  price: number
  volume: number
  eventTime: string
}

// Kommt so lange kein Tick, gilt die Verbindung als tot. Nötig, weil nicht jeder Proxy einen harten Abbruch
// des Upstreams weiterreicht (Nuxt-Dev-Proxy bei abgestürztem Quote-Service) – EventSource sähe dann nie einen Fehler.
// ponytail: Watchdog im Client statt Server-Heartbeat; reicht, solange aktive Instrumente mehrmals pro Sekunde ticken.
// Ausbau, sobald Instrumente ohne Ticks vorkommen (Delisting, Runde 4): Heartbeat-Event vom Server.
const STALE_MS = 10_000

/** Live-Kurse per Server-Sent Events. EventSource verbindet sich nach Fehlern selbst neu, der Watchdog nach Stille. */
export function useQuoteStream(instrumentId: string) {
  const tick = ref<Tick | null>(null)
  const previous = ref<Tick | null>(null)
  const connected = ref(false)
  let source: EventSource | undefined
  let watchdog: ReturnType<typeof setTimeout> | undefined

  function arm() {
    clearTimeout(watchdog)
    watchdog = setTimeout(() => {
      connected.value = false
      connect()
    }, STALE_MS)
  }

  function connect() {
    source?.close()
    source = new EventSource(`/api/quotes/${instrumentId}/stream`)
    source.onopen = () => { connected.value = true }
    source.onerror = () => { connected.value = false }
    source.addEventListener('tick', (e) => {
      connected.value = true
      previous.value = tick.value
      tick.value = JSON.parse((e as MessageEvent<string>).data)
      arm()
    })
    arm()
  }

  onMounted(connect)
  onUnmounted(() => {
    clearTimeout(watchdog)
    source?.close()
  })

  return { tick, previous, connected }
}
