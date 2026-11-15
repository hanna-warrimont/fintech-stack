import { defineVitestConfig } from '@nuxt/test-utils/config'

// Umgebung "nuxt": Auto-Imports, Komponenten und mountSuspended funktionieren wie in der App.
export default defineVitestConfig({ test: { environment: 'nuxt' } })
