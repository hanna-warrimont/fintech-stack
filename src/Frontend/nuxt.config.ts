// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  modules: ['@nuxt/eslint', '@nuxt/test-utils/module'],
  devtools: { enabled: true },
  app: { head: { htmlAttrs: { lang: 'de' }, title: 'Instrumenten-Portal' } },
  compatibilityDate: '2025-07-15',
})
