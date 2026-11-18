import { registerEndpoint } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api, startSession, useUser } from '~/entities/session'

// Kein $fetch-Mock: getestet wird gegen simulierte BFF-Endpunkte.
let me: object | Error = { isAuthenticated: true, name: 'Ada', roles: ['pfleger'] }
registerEndpoint('/api/bff/user', () => {
  if (me instanceof Error) throw me
  return me
})
registerEndpoint('/api/protected', () => { throw createError({ statusCode: 401 }) })

// Der Start lief schon einmal; hier läuft er erneut gegen den jeweils gesetzten BFF-Zustand.
function start() {
  const session = useUser()
  session.user.value = null; session.status.value = 'loading'; session.expired.value = false
  startSession()
  return session
}

describe('session', () => {
  beforeEach(() => { me = { isAuthenticated: true, name: 'Ada', roles: ['pfleger'] } })

  it('loads the user once at start', async () => {
    const { status, hasRole } = start()
    await vi.waitFor(() => expect(status.value).toBe('signedIn'))
    expect(hasRole('pfleger')).toBe(true)
    expect(hasRole('freigeber')).toBe(false)
  })

  it('is anonymous when nobody is signed in', async () => {
    me = { isAuthenticated: false, name: null, roles: [] }
    const { status } = start()
    await vi.waitFor(() => expect(status.value).toBe('anonymous'))
  })

  it('is unavailable, not anonymous, when the BFF is down', async () => {
    me = createError({ statusCode: 502 })
    const { status } = start()
    await vi.waitFor(() => expect(status.value).toBe('unavailable'))
  })

  it('ends a signed-in session on any 401', async () => {
    const { status, expired, user, hasRole } = start()
    await vi.waitFor(() => expect(status.value).toBe('signedIn'))

    await api('/api/protected').catch(() => {})

    expect(status.value).toBe('anonymous')
    expect(expired.value).toBe(true)
    expect(user.value?.isAuthenticated).toBe(false)
    expect(hasRole('pfleger')).toBe(false)
  })

  it('does not call an anonymous visitor expired', async () => {
    me = { isAuthenticated: false, name: null, roles: [] }
    const { status, expired } = start()
    await vi.waitFor(() => expect(status.value).toBe('anonymous'))

    await api('/api/protected').catch(() => {})

    expect(expired.value).toBe(false)
  })
})
