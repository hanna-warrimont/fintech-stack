import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import QuoteTicker from '~/components/QuoteTicker.vue'
import type { Tick } from '~/composables/useQuoteStream'

const tick = (price: number): Tick =>
  ({ instrumentId: 'i', seq: 1, price, volume: 1, eventTime: '2026-11-15T12:00:00Z' })

describe('QuoteTicker', () => {
  it('shows the price in the currency and the direction for screen readers', async () => {
    const ticker = await mountSuspended(QuoteTicker, {
      props: { tick: tick(241), previous: tick(240), connected: true, currency: 'EUR' },
    })

    expect(ticker.text()).toMatch(/241,00\s€/) // Intl setzt ein geschütztes Leerzeichen
    expect(ticker.find('.sr-only').text()).toBe('steigend')
  })

  it('says it is connecting before the first tick', async () => {
    const ticker = await mountSuspended(QuoteTicker, {
      props: { tick: null, previous: null, connected: false, currency: 'EUR' },
    })

    expect(ticker.text()).toBe('Verbindung wird hergestellt …')
  })
})
