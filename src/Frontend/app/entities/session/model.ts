export interface User { isAuthenticated: boolean, name: string | null, roles: string[] }

export type SessionStatus = 'loading' | 'anonymous' | 'signedIn' | 'unavailable'

const anonymousUser: User = { isAuthenticated: false, name: null, roles: [] }

/**
 * Wer ist angemeldet? Das BFF kennt die Tokens, das Frontend sieht nur Name und Rollen.
 * `loading` und `unavailable` (BFF nicht erreichbar) sind bewusst nicht `anonymous`.
 * Verändert wird der Zustand nur hier: `startSession` lädt einmal beim Start, `api` beendet die Sitzung bei 401.
 */
export function useUser() {
  const user = useState<User | null>('user', () => null)
  const status = useState<SessionStatus>('session-status', () => 'loading')
  const expired = useState('session-expired', () => false)
  const hasRole = (role: string) => user.value?.roles.includes(role) ?? false
  return { user, status, expired, hasRole }
}

/**
 * $fetch für Aufrufe, die eine Anmeldung brauchen (send, Arbeitsliste). Ein 401 heißt: Die BFF-Sitzung ist
 * vorbei (das Token lebt nur so lange wie sie). Ein globales $fetch zu überschreiben geht nicht, Nuxt bindet es beim Start.
 */
export const api = $fetch.create({
  onResponseError({ response }) {
    const { user, status, expired } = useUser()
    // Nur eine angemeldete Sitzung kann ablaufen: Anonyme bekommen auf geschützten Routen auch 401.
    if (response.status !== 401 || status.value !== 'signedIn') return
    user.value = anonymousUser
    status.value = 'anonymous'
    expired.value = true
  },
})

/** Einmal beim Start (plugins/session.client.ts); nicht awaiten, der Start soll nicht auf das BFF warten. */
// ponytail: kein automatischer Retry bei 'unavailable'; Neuladen genügt
export function startSession() {
  const { user, status } = useUser()
  return $fetch<User>('/api/bff/user').then(
    (u) => { user.value = u; status.value = u.isAuthenticated ? 'signedIn' : 'anonymous' },
    () => { status.value = 'unavailable' },
  )
}

/** Schreibende Aufrufe ans BFF: immer mit CSRF-Header. Liefert die Fehlermeldung des Servers oder null. */
export async function send(url: string, body?: Record<string, unknown>): Promise<string | null> {
  try {
    await api(url, { method: 'POST', body, headers: { 'X-CSRF': '1' } })
    return null
  }
  catch (e) {
    // Form eines FetchError: HTTP-Status und der Body (ProblemDetails mit title).
    const { status, data } = e as { status?: number, data?: { title?: string } }
    return data?.title ?? (status === 403 ? 'Dafür fehlt dir die Rolle.' : 'Aktion fehlgeschlagen.')
  }
}
