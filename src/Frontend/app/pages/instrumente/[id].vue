<script setup lang="ts">
interface Instrument { id: string, isin: string, name: string, currency: string }

const id = useRoute().params.id as string
const { data: instrument, error } = await useFetch<Instrument>(`/api/instruments/${id}`)
const { tick, previous, connected } = useQuoteStream(id)

const direction = computed(() => {
  if (!tick.value || !previous.value || tick.value.price === previous.value.price) return 'gleich'
  return tick.value.price > previous.value.price ? 'steigt' : 'faellt'
})
const price = computed(() => tick.value && instrument.value
  ? new Intl.NumberFormat('de-DE', { style: 'currency', currency: instrument.value.currency }).format(tick.value.price)
  : null)
const time = computed(() => tick.value
  ? new Date(tick.value.eventTime).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
  : null)
</script>

<template>
  <p><NuxtLink to="/">← Alle Instrumente</NuxtLink></p>

  <template v-if="error">
    <h1>Instrument nicht gefunden</h1>
  </template>

  <template v-else-if="instrument">
    <h1>{{ instrument.name }}</h1>
    <p>ISIN {{ instrument.isin }} · {{ instrument.currency }}</p>

    <!-- ponytail: kein aria-live, 2 Updates/s wären für Screenreader zu laut -->
    <section class="ticker" aria-label="Aktueller Kurs">
      <p v-if="!connected && !tick">Verbindung wird hergestellt …</p>
      <template v-else-if="tick">
        <span class="price" :class="direction">
          <span aria-hidden="true">{{ direction === 'steigt' ? '▲' : direction === 'faellt' ? '▼' : '■' }}</span>
          {{ price }}
        </span>
        <span class="sr-only">{{ direction === 'steigt' ? 'steigend' : direction === 'faellt' ? 'fallend' : 'unverändert' }}</span>
        <small>Stand {{ time }}</small>
        <small v-if="!connected"> · Verbindung wird hergestellt …</small>
      </template>
      <p v-else>Warte auf ersten Kurs …</p>
    </section>
  </template>
</template>

<style scoped>
.ticker { display: flex; align-items: baseline; gap: 1rem; flex-wrap: wrap; }
.price { font-size: 2.5rem; font-variant-numeric: tabular-nums; }
.steigt { color: #1a7f37; }
.faellt { color: #cf222e; }
@media (prefers-color-scheme: dark) { .steigt { color: #3fb950; } .faellt { color: #f85149; } }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
