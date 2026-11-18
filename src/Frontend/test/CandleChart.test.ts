import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it, vi } from 'vitest'
import { CandleChart } from '~/widgets/candle-chart'

const { fetch, series } = vi.hoisted(() => ({ fetch: vi.fn(), series: { setData: vi.fn(), update: vi.fn() } }))
mockNuxtImport('$fetch', () => Object.assign(fetch, { create: () => fetch })) // create: das Session-Plugin umwickelt $fetch
vi.mock('lightweight-charts', () => ({
  CandlestickSeries: {},
  TickMarkType: { Time: 3 },
  createChart: () => ({ addSeries: () => series, timeScale: () => ({ fitContent() {} }), remove() {} }),
}))

const tick = (price: number, eventTime: string) => ({ instrumentId: 'i', seq: 1, price, volume: 1, eventTime })
const candle = { time: 1_763_380_800, open: 10, high: 12, low: 9, close: 11, volume: 5 } // 12:00:00Z

describe('CandleChart', () => {
  it('loads history and lets the current minute grow with live ticks', async () => {
    fetch.mockResolvedValue([candle])
    const chart = await mountSuspended(CandleChart, { props: { instrumentId: 'i', tick: null } })
    await flushPromises()
    expect(series.setData).toHaveBeenCalledOnce()

    await chart.setProps({ tick: tick(13, '2025-11-17T12:00:30Z') })
    expect(series.update).toHaveBeenLastCalledWith({ ...candle, high: 13, close: 13 })

    await chart.setProps({ tick: tick(1, '2025-11-17T11:59:00Z') }) // verspätet: ignoriert
    expect(series.update).toHaveBeenCalledOnce()
  })

  it('shows an error when history fails', async () => {
    fetch.mockRejectedValue(new Error('down'))
    const chart = await mountSuspended(CandleChart, { props: { instrumentId: 'i', tick: null } })
    await flushPromises()
    expect(chart.text()).toContain('Kursverlauf nicht verfügbar.')
  })
})
