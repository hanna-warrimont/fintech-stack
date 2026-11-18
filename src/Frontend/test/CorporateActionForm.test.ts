import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it, vi } from 'vitest'
import { CorporateActionForm } from '~/features/create-corporate-action'

const { fetch } = vi.hoisted(() => ({ fetch: vi.fn() }))
mockNuxtImport('$fetch', () => Object.assign(fetch, { create: () => fetch })) // create: das Session-Plugin umwickelt $fetch

describe('CorporateActionForm', () => {
  it('posts the split for the searched instrument and reports success', async () => {
    fetch.mockImplementation(async (url: string) => url === '/api/search' ? [{ id: 'i1', isin: 'DE0001', name: 'Alpha' }] : undefined)
    const form = await mountSuspended(CorporateActionForm)

    await form.find('#ca-search').setValue('alp')
    await flushPromises()
    await form.find('#ca-instrument').setValue('i1')
    await form.find('form').trigger('submit')
    await flushPromises()

    const [, options] = fetch.mock.calls.find(([url]) => url === '/api/corporate-actions')!
    expect(options.body).toMatchObject({ instrumentId: 'i1', type: 'Split', splitOld: 1, splitNew: 4, newIsin: null })
    expect(form.emitted('created')![0]).toEqual([null])
  })
})
