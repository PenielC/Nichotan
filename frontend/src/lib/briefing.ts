export type BriefingUrgency = 'Overdue' | 'Today' | 'Soon'
export type BriefingKind = 'Biometrics' | 'Pal' | 'Document' | 'Task'

export interface BriefingItem {
  kind: BriefingKind
  urgency: BriefingUrgency
  title: string
  detail: string
  path: string
  daysLate: number
}

export interface Briefing {
  headline: string
  overdue: number
  today: number
  soon: number
  items: BriefingItem[]
}

export const urgencyLabel: Record<BriefingUrgency, string> = { Overdue: 'Overdue', Today: 'Today', Soon: 'Coming up' }
