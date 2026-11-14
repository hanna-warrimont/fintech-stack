<script setup lang="ts">
interface Instrument { id: string, isin: string, name: string, currency: string }

const id = useRoute().params.id as string
// Ein Stream für Ticker UND Chart – kein zweiter EventSource pro Seite.
const { tick, previous, connected } = useQuoteStream(id)
const { data: instrument, error } = await useFetch<Instrument>(`/api/instruments/${id}`)
</script>

<template>
  <p><NuxtLink to="/">← Alle Instrumente</NuxtLink></p>

  <template v-if="error">
    <h1>Instrument nicht gefunden</h1>
  </template>

  <template v-else-if="instrument">
    <h1>{{ instrument.name }}</h1>
    <p>ISIN {{ instrument.isin }} · {{ instrument.currency }}</p>
    <QuoteTicker :tick="tick" :previous="previous" :connected="connected" :currency="instrument.currency" />
    <ClientOnly>
      <CandleChart :instrument-id="instrument.id" :tick="tick" />
    </ClientOnly>
  </template>
</template>
