export interface CorporateAction {
  id: string, instrumentId: string, instrumentName: string, isin: string, type: string,
  splitOld: number | null, splitNew: number | null, newIsin: string | null,
  effectiveAt: string, status: string, createdBy: string, decidedBy: string | null
}

export const statusLabels: Record<string, string> = {
  Draft: 'Entwurf', Submitted: 'Eingereicht', Approved: 'Freigegeben', Rejected: 'Abgelehnt',
  Cancelled: 'Storniert', Discarded: 'Verworfen', Effective: 'Wirksam',
}

export const typeLabels: Record<string, string> = { Split: 'Split', IsinChange: 'ISIN-Wechsel', Delisting: 'Delisting' }

export const details = (a: CorporateAction) =>
  a.type === 'Split' ? `${a.splitOld}:${a.splitNew}` : a.type === 'IsinChange' ? `neue ISIN ${a.newIsin}` : ''

export const localTime = (iso: string) =>
  new Date(iso).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' })

/** Schreibende Aufrufe ans BFF: immer mit CSRF-Header. Liefert die Fehlermeldung des Servers oder null. */
export async function send(url: string, body?: unknown): Promise<string | null> {
  try {
    await $fetch(url, { method: 'POST', body, headers: { 'X-CSRF': '1' } })
    return null
  }
  catch (e: any) {
    if (e?.status === 401) return 'Sitzung abgelaufen – bitte neu anmelden.'
    return e?.data?.title ?? (e?.status === 403 ? 'Dafür fehlt dir die Rolle.' : 'Aktion fehlgeschlagen.')
  }
}
