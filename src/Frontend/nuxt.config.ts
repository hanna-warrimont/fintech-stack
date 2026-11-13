// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  app: { head: { htmlAttrs: { lang: 'de' }, title: 'Instrumenten-Portal' } },
  // Alles unter /api geht ans BFF (gleiche Origin → später Cookie-Session ohne CORS).
  // ponytail: Proxy-Ziel wird beim Start gelesen, reicht für lokal; Produktions-Build in Runde 5
  routeRules: { '/api/**': { proxy: `${process.env.NUXT_BFF_URL}/api/**` } },
})
