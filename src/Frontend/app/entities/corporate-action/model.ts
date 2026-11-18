import { localTime } from '~/shared/lib/localTime'

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

/** Statuszeile einer Kapitalmaßnahme auf der Produktseite. */
export const pdpStatus = (a: CorporateAction) =>
  a.status === 'Approved' ? `Freigegeben, wirksam ab ${localTime(a.effectiveAt)}`
    : a.status === 'Effective' ? `Wirksam seit ${localTime(a.effectiveAt)}`
      : statusLabels[a.status] ?? a.status
