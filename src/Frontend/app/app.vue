<script setup lang="ts">
const { user } = useUser()
const route = useRoute()
</script>

<template>
  <header class="top">
    <nav aria-label="Hauptnavigation">
      <NuxtLink to="/">Instrumente</NuxtLink>
      <NuxtLink v-if="user?.isAuthenticated" to="/kapitalmassnahmen">Kapitalmaßnahmen</NuxtLink>
    </nav>
    <span v-if="user?.isAuthenticated">
      Angemeldet als {{ user.name }} · <a href="/api/bff/logout">Abmelden</a>
    </span>
    <a v-else-if="user" :href="`/api/bff/login?returnUrl=${encodeURIComponent(route.fullPath)}`">Anmelden</a>
  </header>
  <main class="page">
    <NuxtPage />
  </main>
</template>

<style>
:root { color-scheme: light dark; font-family: system-ui, sans-serif; }
body { margin: 0; }
.top { display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap; max-width: 48rem; margin: 0 auto; padding: 1rem 1rem 0; }
.top nav { display: flex; gap: 1rem; }
.page { max-width: 48rem; margin: 0 auto; padding: 1.5rem 1rem; }
a { color: LinkText; }
</style>
