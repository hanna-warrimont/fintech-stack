import { mockNuxtImport } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { send } from '~/utils/corporateActions'

// $fetch ist in Nuxt ein Auto-Import – ersetzt wird er deshalb über mockNuxtImport (vi.hoisted, weil vi.mock hochgezogen wird).
const { fetch } = vi.hoisted(() => ({ fetch: vi.fn() }))
mockNuxtImport('$fetch', () => fetch)

// send() ist die Schreibseite zum BFF: Ohne X-CSRF-Header lehnt das BFF jeden POST mit 400 ab (src/Bff/Csrf.cs).
describe('send', () => {
  afterEach(() => fetch.mockReset())

  it('posts with the CSRF header and returns null on success', async () => {
    fetch.mockResolvedValue(undefined)

    expect(await send('/api/corporate-actions/1/approve', { a: 1 })).toBeNull()
    expect(fetch).toHaveBeenCalledWith('/api/corporate-actions/1/approve',
      { method: 'POST', body: { a: 1 }, headers: { 'X-CSRF': '1' } })
  })

  it.each([
    [{ status: 401 }, 'Sitzung abgelaufen – bitte neu anmelden.'],
    [{ status: 403 }, 'Dafür fehlt dir die Rolle.'],
    [{ status: 409, data: { title: 'Wer eine Maßnahme erfasst hat, darf sie nicht freigeben.' } },
      'Wer eine Maßnahme erfasst hat, darf sie nicht freigeben.'],
    [{ status: 502 }, 'Aktion fehlgeschlagen.'],
  ])('maps %o to a message', async (error, message) => {
    fetch.mockRejectedValue(error)

    expect(await send('/api/x')).toBe(message)
  })
})
