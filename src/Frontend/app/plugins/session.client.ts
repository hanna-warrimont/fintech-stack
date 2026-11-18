import { startSession } from '~/entities/session'

// Nur im Browser: Beim Server-Rendering ginge das Session-Cookie nicht mit. Nicht awaiten (siehe startSession).
export default defineNuxtPlugin(() => { startSession() })
