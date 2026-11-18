<script setup lang="ts">
import { useUser } from '~/entities/session'

const { user, status, expired } = useUser()
const route = useRoute()
const login = computed(() => `/api/bff/login?returnUrl=${encodeURIComponent(route.fullPath)}`)
</script>

<template>
  <header class="top">
    <nav aria-label="Hauptnavigation">
      <NuxtLink to="/">Instrumente</NuxtLink>
      <NuxtLink v-if="status === 'signedIn'" to="/kapitalmassnahmen">Kapitalmaßnahmen</NuxtLink>
    </nav>
    <span v-if="status === 'signedIn'">
      Angemeldet als {{ user?.name }} · <a href="/api/bff/logout">Abmelden</a>
    </span>
    <span v-else-if="status === 'unavailable'">Anmeldestatus nicht verfügbar</span>
    <a v-else-if="status === 'anonymous'" :href="login">Anmelden</a>
  </header>
  <p v-if="expired" role="alert" class="page expired">Sitzung abgelaufen – bitte <a :href="login">neu anmelden</a>.</p>
  <main class="page">
    <NuxtPage />
  </main>
</template>

<style>
:root { color-scheme: light dark; font-family: system-ui, sans-serif; }
body { margin: 0; }
.top { display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap; max-width: 48rem; margin: 0 auto; padding: 1rem 1rem 0; }
.top nav { display: flex; gap: 1rem; }
.expired { padding-block: 0; }
.page { max-width: 48rem; margin: 0 auto; padding: 1.5rem 1rem; }
a { color: LinkText; }
</style>
