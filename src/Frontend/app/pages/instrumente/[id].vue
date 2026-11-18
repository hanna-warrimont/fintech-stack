<script setup lang="ts">
import { details, pdpStatus, typeLabels, type CorporateAction } from '~/entities/corporate-action'
import { QuoteTicker, useQuoteStream } from '~/entities/quote'
import { CandleChart } from '~/widgets/candle-chart'

interface Instrument { id: string, isin: string, name: string, currency: string, status: string }
interface Pdp { instrument: Instrument, corporateActions: CorporateAction[] }

const id = useRoute().params.id as string
// Ein Stream für Ticker UND Chart – kein zweiter EventSource pro Seite.
const { tick, previous, connected } = useQuoteStream(id)
// PDP-Aggregation im BFF: Stammdaten und Kapitalmaßnahmen in einem Aufruf.
const { data, error, refresh } = await useFetch<Pdp>(`/api/pdp/${id}`)

// Eventual Consistency: Freigegeben → (Stichtag, Job, Kafka) → Wirksam → History passt den Chart an.
// Solange etwas freigegeben ist, alle 10 s nachsehen; wird etwas wirksam, den Chart neu laden.
const effectiveCount = computed(() => data.value?.corporateActions.filter(a => a.status === 'Effective').length ?? 0)
const chartKey = ref(0)
watch(effectiveCount, () => { chartKey.value++ })
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  timer = setInterval(() => { if (data.value?.corporateActions.some(a => a.status === 'Approved')) refresh() }, 10_000)
})
onUnmounted(() => clearInterval(timer))
</script>

<template>
  <div>
    <p><NuxtLink to="/">← Alle Instrumente</NuxtLink></p>

    <template v-if="error">
      <h1>Instrument nicht gefunden</h1>
    </template>

    <template v-else-if="data">
      <h1>{{ data.instrument.name }}</h1>
      <p>
        ISIN {{ data.instrument.isin }} · {{ data.instrument.currency }}
        <strong v-if="data.instrument.status === 'Delisted'"> · Delistet</strong>
      </p>
      <QuoteTicker :tick="tick" :previous="previous" :connected="connected" :currency="data.instrument.currency" />
      <ClientOnly>
        <CandleChart :key="chartKey" :instrument-id="data.instrument.id" :tick="tick" />
      </ClientOnly>

      <section v-if="data.corporateActions.length" aria-labelledby="ca-title">
        <h2 id="ca-title">Kapitalmaßnahmen</h2>
        <ul aria-live="polite">
          <li v-for="a in data.corporateActions" :key="a.id">
            {{ typeLabels[a.type] ?? a.type }} {{ details(a) }} –
            {{ pdpStatus(a) }}
        </li>
        </ul>
      </section>
    </template>
  </div>
</template>
