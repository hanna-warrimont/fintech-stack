<script setup lang="ts">
interface SearchHit { id: string, isin: string, wkn: string | null, name: string, type: string, currency: string, status: string }

const typeLabels: Record<string, string> = { Share: 'Aktie', Bond: 'Anleihe', Fund: 'Fonds', Etf: 'ETF' }

const q = ref('')
const debounced = ref('')
let timer: ReturnType<typeof setTimeout> | undefined
watch(q, (value) => {
  clearTimeout(timer)
  timer = setTimeout(() => { debounced.value = value }, 250)
})

const { data: hits, error } = await useFetch<SearchHit[]>('/api/search', { query: { q: debounced } })
</script>

<template>
  <div>
    <h1>Instrumente</h1>
    <p>
      <label for="q">Instrument suchen</label><br>
      <input id="q" v-model="q" type="search" placeholder="Name, ISIN oder WKN" autocomplete="off">
    </p>

    <p v-if="error">Suche nicht verfügbar.</p>
    <p v-else-if="hits && hits.length === 0">Keine Treffer</p>
    <ul v-else>
      <li v-for="i in hits" :key="i.id">
        <NuxtLink :to="`/instrumente/${i.id}`">{{ i.name }}</NuxtLink>
        <small> · {{ i.isin }}<template v-if="i.wkn"> · WKN {{ i.wkn }}</template> · {{ typeLabels[i.type] ?? i.type }}</small>
      </li>
    </ul>
  </div>
</template>

<style scoped>
input { font: inherit; padding: 0.4rem 0.6rem; width: min(100%, 24rem); }
</style>
