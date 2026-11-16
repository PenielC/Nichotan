import type { DocRef } from './admissions'
import type { DestinationCountry } from './application'
import type { DocumentGaps } from './documents'

export type VisaStatus =
  | 'Preparing'
  | 'Submitted'
  | 'BiometricsRequested'
  | 'BiometricsCompleted'
  | 'AdditionalInformationRequested'
  | 'Approved'
  | 'Refused'
  | 'Withdrawn'

export const visaStatusLabel: Record<VisaStatus, string> = {
  Preparing: 'Preparing',
  Submitted: 'Submitted',
  BiometricsRequested: 'Biometrics requested',
  BiometricsCompleted: 'Biometrics completed',
  AdditionalInformationRequested: 'More information requested',
  Approved: 'Approved',
  Refused: 'Refused',
  Withdrawn: 'Withdrawn',
}

export const allVisaStatuses = Object.keys(visaStatusLabel) as VisaStatus[]

export interface VisaSummary {
  id: string
  applicationId: string
  candidateId: string
  candidateName: string
  institutionName: string
  programmeName: string
  country: DestinationCountry
  status: VisaStatus
  statusChangedAtUtc: string
  referenceNumber: string | null
  submittedOn: string | null
  biometricsAppointmentOn: string | null
  decisionOn: string | null
}

export interface VisaEvent {
  id: string
  from: VisaStatus | null
  to: VisaStatus
  occurredOn: string
  recordedAtUtc: string
  recordedBy: string | null
  note: string | null
  isCorrection: boolean
}

export interface VisaDetail {
  summary: VisaSummary
  biometricsLocation: string | null
  openedOn: string
  allowedMoves: { status: VisaStatus; noteRequired: boolean }[]
  history: VisaEvent[]
  /** Visa documents still to be accepted. */
  documentGaps: DocumentGaps
  biometricsLetter: DocRef | null
  decisionLetter: DocRef | null
}

export interface VisaStats {
  inProcess: number
  approved: number
  preparing: number
}

export interface MyVisa {
  id: string
  applicationId: string
  institutionName: string
  programmeName: string
  country: DestinationCountry
  status: VisaStatus
  statusChangedAtUtc: string
  biometricsAppointmentOn: string | null
  biometricsLocation: string | null
  history: { status: VisaStatus; occurredOn: string }[]
  documentsMissing: number
  canUploadBiometricsLetter: boolean
  biometricsLetter: { documentId: string; fileName: string } | null
  biometricsLetterTypeId: string | null
  decisionLetter: { documentId: string; fileName: string } | null
  /** Submitted, biometrics not yet done: the student can say they have been. */
  canMarkBiometricsDone: boolean
}

/** A lodged visa application whose biometrics aren't marked done yet: the staff reminder. */
export interface BiometricsFollowUp {
  visaCaseId: string
  applicationId: string
  candidateId: string
  candidateName: string
  institutionName: string
  status: VisaStatus
  submittedOn: string | null
  appointmentOn: string | null
  daysWaiting: number
  overdue: boolean
}

export type PreDepartureImportance = 'Required' | 'Recommended'

export interface PreDepartureItem {
  id: string
  title: string
  detail: string | null
  importance: PreDepartureImportance
  source: 'Template' | 'Manual'
  isActive: boolean
  note: string | null
  confirmedAtUtc: string | null
  verifiedAtUtc: string | null
  verifiedBy: string | null
  completedAtUtc: string | null
}

/** A visa case's pre-departure list. Not activated until staff open it after the visa is approved. */
export interface PreDepartureResult {
  visaCaseId: string
  country: DestinationCountry
  institutionName: string
  activated: boolean
  canActivate: boolean
  plannedDepartureOn: string | null
  caveat: string | null
  readyToTravel: boolean
  items: PreDepartureItem[]
}

export interface PreDepartureTemplateItem {
  id: string
  country: DestinationCountry
  title: string
  detail: string | null
  importance: PreDepartureImportance
  sortOrder: number
  isActive: boolean
}

export interface PreDepartureTemplate {
  items: PreDepartureTemplateItem[]
  caveats: { country: DestinationCountry; text: string }[]
}
