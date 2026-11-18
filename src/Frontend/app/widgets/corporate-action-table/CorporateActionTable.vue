<script setup lang="ts">
import { details, statusLabels, typeLabels, type CorporateAction } from '~/entities/corporate-action'
import { useUser } from '~/entities/session'
import { localTime } from '~/shared/lib/localTime'

const { hasRole } = useUser()
defineProps<{ actions: CorporateAction[] }>()
const visibleSteps = (a: CorporateAction) => (steps[a.status] ?? []).filter(s => hasRole(s.role))
const decided = (a: CorporateAction) => a.decidedBy ? `${a.createdBy} / ${a.decidedBy}` : a.createdBy
const emit = defineEmits<{ step: [action: CorporateAction, name: string] }>()

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
          <td>{{ decided(a) }}</td>
          <td>
            <button v-for="s in visibleSteps(a)" :key="s.name" type="button" @click="emit('step', a, s.name)">{{ s.label }}</button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.table { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; }
th, td { text-align: left; padding: .4rem .5rem; border-bottom: 1px solid color-mix(in srgb, currentColor 20%, transparent); vertical-align: top; }
td button { margin: 0 .25rem .25rem 0; font: inherit; }
</style>
