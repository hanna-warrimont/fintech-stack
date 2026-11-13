<script setup lang="ts">
interface Instrument { id: string, isin: string, name: string, currency: string }

const id = useRoute().params.id as string
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
    <QuoteTicker :instrument-id="instrument.id" :currency="instrument.currency" />
  </template>
</template>
