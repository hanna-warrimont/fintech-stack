// Alles unter /api geht ans BFF (gleiche Origin → später Cookie-Session ohne CORS).
// Eigene Route statt routeRules.proxy: Der eingebaute Proxy bricht den Upstream-Request nicht ab, wenn der Browser
// einen SSE-Stream beendet – jeder geschlossene Tab hinterließ ein offenes Abo im Quote-Service.
// ponytail: Ziel kommt aus NUXT_BFF_URL beim Start; reicht für lokal, Produktions-Hosting in Runde 5
export default defineEventHandler((event) => {
  const upstream = new AbortController()
  event.node.res.on('close', () => upstream.abort())
  return proxyRequest(event, `${process.env.NUXT_BFF_URL}${event.path}`, { fetchOptions: { signal: upstream.signal } })
})
