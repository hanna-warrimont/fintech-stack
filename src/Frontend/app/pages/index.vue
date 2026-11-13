<script setup lang="ts">
interface Instrument { id: string, isin: string, name: string, currency: string }
const { data: instruments, error } = await useFetch<Instrument[]>('/api/instruments')
</script>

<template>
  <h1>Instrumente</h1>
  <p v-if="error">Instrumente konnten nicht geladen werden.</p>
  <ul v-else>
    <li v-for="i in instruments" :key="i.id">
      <NuxtLink :to="`/instrumente/${i.id}`">{{ i.name }}</NuxtLink>
      <small> · {{ i.isin }}</small>
    </li>
  </ul>
</template>
