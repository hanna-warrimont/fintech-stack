<script setup lang="ts">
interface SearchHit { id: string, isin: string, name: string }

const { user, hasRole } = useUser()
const actions = ref<CorporateAction[]>([])
const message = ref<string | null>(null)

async function load() {
  try {
    actions.value = await $fetch<CorporateAction[]>('/api/corporate-actions')
  }
  catch (e: any) {
    message.value = e?.status === 401 ? 'Sitzung abgelaufen – bitte neu anmelden.' : 'Arbeitsliste nicht verfügbar.'
  }
}

// Eventual Consistency sichtbar machen: Solange etwas freigegeben, aber noch nicht wirksam ist, alle 10 s nachsehen.
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  load()
  timer = setInterval(() => { if (actions.value.some(a => a.status === 'Approved')) load() }, 10_000)
})
onUnmounted(() => clearInterval(timer))

async function step(a: CorporateAction, name: string) {
  message.value = await send(`/api/corporate-actions/${a.id}/${name}`)
  await load()
}

// Neue Maßnahme (nur Pfleger)
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
  message.value = await send('/api/corporate-actions', {
    instrumentId: form.instrumentId,
    type: form.type,
    splitOld: form.type === 'Split' ? form.splitOld : null,
    splitNew: form.type === 'Split' ? form.splitNew : null,
    newIsin: form.type === 'IsinChange' ? form.newIsin : null,
    effectiveAt: new Date(form.effectiveAt).toISOString(),
  })
  if (!message.value) form.effectiveAt = inMinutes(3)
  await load()
}

const steps: Record<string, { name: string, label: string, role: string }[]> = {
  Draft: [{ name: 'submit', label: 'Einreichen', role: 'pfleger' }, { name: 'discard', label: 'Verwerfen', role: 'pfleger' }],
  Submitted: [
    { name: 'withdraw', label: 'Zurückziehen', role: 'pfleger' },
    { name: 'approve', label: 'Freigeben', role: 'freigeber' },
    { name: 'reject', label: 'Ablehnen', role: 'freigeber' },
  ],
  Approved: [{ name: 'cancel', label: 'Stornieren', role: 'freigeber' }],
}
</script>

<template>
  <h1>Kapitalmaßnahmen</h1>

  <p v-if="user && !user.isAuthenticated">
    Bitte <a href="/api/bff/login?returnUrl=/kapitalmassnahmen">anmelden</a>.
  </p>

  <template v-else-if="user">
    <p v-if="message" role="alert" class="alert">{{ message }}</p>

    <form v-if="hasRole('pfleger')" class="create" @submit.prevent="create">
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

    <h2>Arbeitsliste</h2>
    <p v-if="actions.length === 0">Keine Kapitalmaßnahmen.</p>
    <div v-else class="table">
      <table>
        <thead>
          <tr><th>Instrument</th><th>Art</th><th>Wirksam ab</th><th>Status</th><th>Erfasst / entschieden</th><th>Aktionen</th></tr>
        </thead>
        <tbody>
          <tr v-for="a in actions" :key="a.id">
            <td>{{ a.instrumentName }}<br><small>{{ a.isin }}</small></td>
            <td>{{ typeLabels[a.type] ?? a.type }} {{ details(a) }}</td>
            <td>{{ localTime(a.effectiveAt) }}</td>
            <td>{{ statusLabels[a.status] ?? a.status }}</td>
            <td>{{ a.createdBy }}<template v-if="a.decidedBy"> / {{ a.decidedBy }}</template></td>
            <td>
              <template v-for="s in steps[a.status] ?? []" :key="s.name">
                <button v-if="hasRole(s.role)" type="button" @click="step(a, s.name)">{{ s.label }}</button>
              </template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </template>
</template>

<style scoped>
.alert { padding: .5rem .75rem; border: 1px solid #cf222e; border-radius: .25rem; }
.create { margin-bottom: 2rem; }
input, select, button { font: inherit; }
input[type="number"] { width: 5rem; }
.table { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; }
th, td { text-align: left; padding: .4rem .5rem; border-bottom: 1px solid color-mix(in srgb, currentColor 20%, transparent); vertical-align: top; }
td button { margin: 0 .25rem .25rem 0; }
</style>
