<script setup lang="ts">
import { CandlestickSeries, createChart, TickMarkType, type CandlestickData, type IChartApi, type ISeriesApi, type Time, type UTCTimestamp } from 'lightweight-charts'
import type { Tick } from '~/entities/quote'

interface Candle { time: number, open: number, high: number, low: number, close: number, volume: number }

const props = defineProps<{ instrumentId: string, tick: Tick | null }>()

const intervals = [
  { value: '1m', label: '1 Min' },
  { value: '1h', label: '1 Std' },
  { value: '1d', label: '1 Tag' },
] as const
const interval = ref<(typeof intervals)[number]['value']>('1m')
const state = ref<'loading' | 'ok' | 'empty' | 'error'>('loading')
const el = ref<HTMLElement>()
let chart: IChartApi | undefined
let series: ISeriesApi<'Candlestick'> | undefined
let last: CandlestickData<UTCTimestamp> | undefined

const palette = {
  dark: { text: '#d1d5db', grid: '#374151', up: '#3fb950', down: '#f85149' },
  light: { text: '#1f2937', grid: '#e5e7eb', up: '#1a7f37', down: '#cf222e' },
}

// Die Bibliothek zeigt Zeiten in UTC; der Ticker zeigt Ortszeit – also auch hier Ortszeit.
const local = (t: Time, o: Intl.DateTimeFormatOptions) => new Date((t as number) * 1000).toLocaleString('de-DE', o)

async function load() {
  state.value = 'loading'
  const requested = interval.value
  try {
    const candles = await $fetch<Candle[]>(`/api/history/${props.instrumentId}`, { query: { interval: requested } })
    if (requested !== interval.value) return // inzwischen anderes Intervall gewählt: veraltete Antwort verwerfen
    const data = candles.map(c => ({ ...c, time: c.time as UTCTimestamp }))
    series!.setData(data)
    last = data.at(-1)
    chart!.timeScale().fitContent()
    state.value = data.length ? 'ok' : 'empty'
  }
  catch {
    if (requested === interval.value) state.value = 'error'
  }
}

onMounted(() => {
  // ponytail: Farbschema beim Laden gelesen; ein Wechsel zur Laufzeit braucht Neuladen
  const c = matchMedia('(prefers-color-scheme: dark)').matches ? palette.dark : palette.light
  chart = createChart(el.value!, {
    autoSize: true, // Größe kommt aus dem Container (CSS-Höhe unten)
    localization: { locale: 'de-DE', timeFormatter: (t: Time) => local(t, { dateStyle: 'short', timeStyle: 'short' }) },
    layout: { background: { color: 'transparent' }, textColor: c.text },
    grid: { vertLines: { color: c.grid }, horzLines: { color: c.grid } },
    timeScale: {
      timeVisible: true,
      tickMarkFormatter: (t: Time, type: TickMarkType) => local(t, type >= TickMarkType.Time
        ? { hour: '2-digit', minute: '2-digit' }
        : type === TickMarkType.DayOfMonth ? { day: '2-digit', month: '2-digit' } : { month: 'short', year: 'numeric' }),
    },
  })
  series = chart.addSeries(CandlestickSeries, {
    upColor: c.up, wickUpColor: c.up, borderUpColor: c.up,
    downColor: c.down, wickDownColor: c.down, borderDownColor: c.down,
  })
  load()
})
onUnmounted(() => chart?.remove())
watch(interval, load)

// Die laufende Minute wächst mit dem Live-Tick mit. Nur bei 1 Min: Stunden-/Tageskerzen kommen beim nächsten Laden.
// Auch bei 'empty': Der erste Live-Tick eines neuen Instruments eröffnet die erste Kerze.
const accepting = computed(() => interval.value === '1m' && ['ok', 'empty'].includes(state.value))
watch(() => props.tick, (tick) => {
  if (!tick || !series || !accepting.value) return
  const time = (Math.floor(Date.parse(tick.eventTime) / 60_000) * 60) as UTCTimestamp
  if (last && time < last.time) return // verspäteter Tick: der Chart kann nur die letzte Kerze ändern, History ordnet ihn ein
  last = last?.time === time
    ? { ...last, high: Math.max(last.high, tick.price), low: Math.min(last.low, tick.price), close: tick.price }
    : { time, open: tick.price, high: tick.price, low: tick.price, close: tick.price }
  series.update(last)
  state.value = 'ok'
})
</script>

<template>
  <section class="chart" aria-label="Kursverlauf">
    <div class="intervals">
      <button
        v-for="i in intervals" :key="i.value" type="button"
        :aria-pressed="interval === i.value" @click="interval = i.value"
      >
        {{ i.label }}
      </button>
    </div>
    <p v-if="state === 'empty'">Noch keine Kursdaten.</p>
    <p v-else-if="state === 'error'">Kursverlauf nicht verfügbar.</p>
    <div v-show="state === 'ok' || state === 'loading'" ref="el" class="canvas" role="img" :aria-label="`Kursverlauf ${intervals.find(i => i.value === interval)!.label}`" />
  </section>
</template>

<style scoped>
.chart { margin-top: 2rem; }
.canvas { height: 320px; }
.intervals { display: flex; gap: .5rem; margin-bottom: .5rem; }
button { padding: .25rem .75rem; border: 1px solid currentColor; border-radius: .25rem; background: transparent; color: inherit; cursor: pointer; }
button[aria-pressed="true"] { font-weight: bold; outline: 2px solid currentColor; }
</style>
