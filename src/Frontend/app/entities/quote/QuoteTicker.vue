<script setup lang="ts">
import type { Tick } from './useQuoteStream'

const props = defineProps<{ tick: Tick | null, previous: Tick | null, connected: boolean, currency: string }>()
const tick = toRef(props, 'tick')
const previous = toRef(props, 'previous')

const direction = computed(() => {
  if (!tick.value || !previous.value || tick.value.price === previous.value.price) return 'gleich'
  return tick.value.price > previous.value.price ? 'steigt' : 'faellt'
})
const view = computed(() => ({
  steigt: { icon: '▲', label: 'steigend' },
  faellt: { icon: '▼', label: 'fallend' },
  gleich: { icon: '■', label: 'unverändert' },
})[direction.value])
const price = computed(() => tick.value
  ? new Intl.NumberFormat('de-DE', { style: 'currency', currency: props.currency }).format(tick.value.price)
  : null)
const time = computed(() => tick.value
  ? new Date(tick.value.eventTime).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
  : null)
</script>

<template>
  <!-- ponytail: kein aria-live, 2 Updates/s wären für Screenreader zu laut -->
  <section class="ticker" aria-label="Aktueller Kurs">
    <p v-if="!props.connected && !tick">Verbindung wird hergestellt …</p>
    <template v-else-if="tick">
      <span class="price" :class="direction">
        <span aria-hidden="true">{{ view.icon }}</span>
        {{ price }}
      </span>
      <span class="sr-only">{{ view.label }}</span>
      <small>Stand {{ time }}</small>
      <small v-if="!props.connected"> · Verbindung wird hergestellt …</small>
    </template>
    <p v-else>Warte auf ersten Kurs …</p>
  </section>
</template>

<style scoped>
.ticker { display: flex; align-items: baseline; gap: 1rem; flex-wrap: wrap; }
.price { font-size: 2.5rem; font-variant-numeric: tabular-nums; }
.steigt { color: #1a7f37; }
.faellt { color: #cf222e; }
@media (prefers-color-scheme: dark) { .steigt { color: #3fb950; } .faellt { color: #f85149; } }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
</style>
