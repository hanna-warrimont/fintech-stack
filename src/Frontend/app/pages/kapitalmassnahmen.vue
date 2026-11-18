<script setup lang="ts">
import type { CorporateAction } from '~/entities/corporate-action'
import { api, send, useUser } from '~/entities/session'
import { CorporateActionForm } from '~/features/create-corporate-action'
import { CorporateActionTable } from '~/widgets/corporate-action-table'

const { status, hasRole } = useUser()
const actions = ref<CorporateAction[]>([])
const message = ref<string | null>(null)

async function load() {
  try {
    actions.value = await api<CorporateAction[]>('/api/corporate-actions')
  }
  catch {
    message.value = 'Arbeitsliste nicht verfügbar.'
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

function onCreated(error: string | null) {
  message.value = error
  load()
}
</script>

<template>
  <div>
    <h1>Kapitalmaßnahmen</h1>

    <p v-if="status === 'anonymous'">
      Bitte <a href="/api/bff/login?returnUrl=/kapitalmassnahmen">anmelden</a>.
    </p>

    <template v-else-if="status === 'signedIn'">
      <p v-if="message" role="alert" class="alert">{{ message }}</p>

      <CorporateActionForm v-if="hasRole('pfleger')" @created="onCreated" />

      <h2>Arbeitsliste</h2>
      <CorporateActionTable :actions="actions" @step="step" />
    </template>
  </div>
</template>

<style scoped>
.alert { padding: .5rem .75rem; border: 1px solid #cf222e; border-radius: .25rem; }
</style>
