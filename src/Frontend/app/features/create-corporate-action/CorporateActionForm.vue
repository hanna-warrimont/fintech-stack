<script setup lang="ts">
import { send } from '~/entities/session'
import { typeLabels } from '~/entities/corporate-action'

interface SearchHit { id: string, isin: string, name: string }

const emit = defineEmits<{ created: [error: string | null] }>()

const q = ref('')
const hits = ref<SearchHit[]>([])
watch(q, async (value) => { hits.value = value ? await $fetch<SearchHit[]>('/api/search', { query: { q: value } }).catch(() => []) : [] })
const pad = (n: number) => String(n).padStart(2, '0')
const inMinutes = (m: number) => {
  const d = new Date(Date.now() + m * 60_000)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
const form = reactive({ instrumentId: '', type: 'Split', splitOld: 1, splitNew: 4, newIsin: '', effectiveAt: inMinutes(3) })

async function create() {
  const error = await send('/api/corporate-actions', {
    instrumentId: form.instrumentId,
    type: form.type,
    splitOld: form.type === 'Split' ? form.splitOld : null,
    splitNew: form.type === 'Split' ? form.splitNew : null,
    newIsin: form.type === 'IsinChange' ? form.newIsin : null,
    effectiveAt: new Date(form.effectiveAt).toISOString(),
  })
  if (!error) form.effectiveAt = inMinutes(3)
  emit('created', error)
}
</script>

<template>
  <form class="create" @submit.prevent="create">
    <h2>Neue Kapitalmaßnahme</h2>
    <p>
      <label for="ca-search">Instrument suchen</label><br>
      <input id="ca-search" v-model="q" type="search" autocomplete="off" placeholder="Name, ISIN oder WKN">
    </p>
    <p>
      <label for="ca-instrument">Instrument</label><br>
      <select id="ca-instrument" v-model="form.instrumentId" required>
        <option value="" disabled>– bitte wählen –</option>
        <option v-for="h in hits" :key="h.id" :value="h.id">{{ h.name }} ({{ h.isin }})</option>
      </select>
    </p>
    <p>
      <label for="ca-type">Art</label><br>
      <select id="ca-type" v-model="form.type">
        <option v-for="(label, value) in typeLabels" :key="value" :value="value">{{ label }}</option>
      </select>
    </p>
    <p v-if="form.type === 'Split'">
      <label for="ca-old">Verhältnis alt</label>
      <input id="ca-old" v-model.number="form.splitOld" type="number" min="1" max="1000" required>
      <label for="ca-new">: neu</label>
      <input id="ca-new" v-model.number="form.splitNew" type="number" min="1" max="1000" required>
    </p>
    <p v-if="form.type === 'IsinChange'">
      <label for="ca-isin">Neue ISIN</label><br>
      <input id="ca-isin" v-model="form.newIsin" required maxlength="12">
    </p>
    <p>
      <label for="ca-at">Wirksam ab</label><br>
      <input id="ca-at" v-model="form.effectiveAt" type="datetime-local" required>
    </p>
    <button type="submit">Erfassen</button>
  </form>
</template>

<style scoped>
.create { margin-bottom: 2rem; }
input, select, button { font: inherit; }
input[type="number"] { width: 5rem; }
</style>
