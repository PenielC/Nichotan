import type { ApplicantLevel, DestinationCountry, StartTerm } from './application'
import type { DocumentGaps } from './documents'

export type AdmissionStatus =
  | 'Draft'
  | 'DocumentsRequired'
  | 'ReadyForReview'
  | 'SubmittedToInstitution'
  | 'AwaitingDecision'
  | 'OfferReceived'
  | 'Rejected'
  | 'MoreInformationRequired'
  | 'ResubmissionRequired'
  | 'Withdrawn'
  | 'Closed'

export interface Intake {
  term: StartTerm
  year: number
}

export interface Institution {
  id: string
  name: string
  country: DestinationCountry
  website: string | null
  notes: string | null
  isArchived: boolean
  programmeCount: number
}

export interface Programme {
  id: string
  institutionId: string
  institutionName: string
  country: DestinationCountry
  name: string
  level: ApplicantLevel
  isArchived: boolean
  intakes: Intake[]
}

export interface AdmissionSummary {
  id: string
  candidateId: string
  candidateName: string
  institutionName: string
  programmeName: string
  country: DestinationCountry
  level: ApplicantLevel
  intakeTerm: StartTerm
  intakeYear: number
  status: AdmissionStatus
  statusChangedAtUtc: string
  institutionReference: string | null
  submittedToInstitutionOn: string | null
}

export interface AdmissionEvent {
  id: string
  from: AdmissionStatus | null
  to: AdmissionStatus
  occurredOn: string
  recordedAtUtc: string
  recordedBy: string | null
  note: string | null
  isCorrection: boolean
}

export interface AdmissionDetail {
  summary: AdmissionSummary
  portalUrl: string | null
  formSubmitted: boolean
  allowedMoves: { status: AdmissionStatus; noteRequired: boolean }[]
  history: AdmissionEvent[]
  documentGaps: DocumentGaps
}

export interface AdmissionStats {
  active: number
  /** Applications sitting at "offer received" now. */
  offers: number
  /** Applications that have ever received an offer. */
  offersIssued: number
  total: number
  /** Sent by students and waiting for staff to review and submit. */
  toReview: number
  /** Applications whose formal acceptance letter has been recorded. */
  acceptances: number
  /** Acceptance letter recorded but the PAL hasn't arrived. */
  palFollowUps: number
  palOverdue: number
}

export interface PalFollowUp {
  applicationId: string
  candidateId: string
  candidateName: string
  institutionName: string
  programmeName: string
  acceptanceReceivedOn: string
  daysWaiting: number
  overdue: boolean
}

export interface MyAdmission {
  id: string
  institutionName: string
  programmeName: string
  country: DestinationCountry
  intakeTerm: StartTerm
  intakeYear: number
  status: AdmissionStatus
  statusChangedAtUtc: string
  submittedToInstitutionOn: string | null
  history: { status: AdmissionStatus; occurredOn: string }[]
  /** The student has done everything needed and can send it to their counsellor. */
  canSend: boolean
  canWithdraw: boolean
  /** What still stops them sending it, in plain words. */
  blockers: string[]
  offer: { conditions: string | null; respondByOn: string | null; letterDocumentId: string | null; letterFileName: string | null } | null
  acceptance: { receivedOn: string; palNumber: string | null; letterDocumentId: string | null; palLetterDocumentId: string | null } | null
  /** Once staff have applied to the school, the student can upload the offer letter themselves. */
  canUploadOfferLetter: boolean
  offerLetter: { documentId: string; fileName: string } | null
  offerLetterTypeId: string | null
}

export interface DocRef {
  documentId: string
  fileName: string
  uploadedAtUtc: string
}

export interface Outcome {
  applicationId: string
  status: AdmissionStatus
  palApplies: boolean
  offer: { conditions: string | null; respondByOn: string | null; recordedAtUtc: string; recordedBy: string | null } | null
  acceptance: { receivedOn: string; palNumber: string | null; note: string | null; recordedAtUtc: string; recordedBy: string | null } | null
  offerLetter: DocRef | null
  acceptanceLetter: DocRef | null
  palLetter: DocRef | null
}

export interface DirectoryProgramme {
  id: string
  name: string
  level: ApplicantLevel
  intakes: Intake[]
}

export interface DirectoryInstitution {
  id: string
  name: string
  country: DestinationCountry
  website: string | null
  programmes: DirectoryProgramme[]
}

export interface Suggestion {
  id: string
  candidateId: string
  candidateName: string
  institutionName: string
  country: DestinationCountry
  programmeName: string
  intakeTerm: StartTerm | null
  intakeYear: number | null
  note: string | null
  status: 'New' | 'Added' | 'Declined'
  staffNote: string | null
  createdAtUtc: string
}

export const statusLabel: Record<AdmissionStatus, string> = {
  Draft: 'Draft',
  DocumentsRequired: 'Documents required',
  ReadyForReview: 'Ready for review',
  SubmittedToInstitution: 'Submitted to institution',
  AwaitingDecision: 'Awaiting decision',
  OfferReceived: 'Offer received',
  Rejected: 'Rejected',
  MoreInformationRequired: 'More information required',
  ResubmissionRequired: 'Resubmission required',
  Withdrawn: 'Withdrawn',
  Closed: 'Closed',
}

/** The same statuses in plain words for the candidate. */
export const candidateStatusLabel: Record<AdmissionStatus, string> = {
  Draft: 'Being prepared',
  DocumentsRequired: 'We need documents from you',
  ReadyForReview: 'Being checked by your counsellor',
  SubmittedToInstitution: 'Submitted to the school',
  AwaitingDecision: 'Waiting for the school’s decision',
  OfferReceived: 'Offer received',
  Rejected: 'Not successful',
  MoreInformationRequired: 'The school needs more information',
  ResubmissionRequired: 'Needs to be sent again',
  Withdrawn: 'Withdrawn',
  Closed: 'Closed',
}

export const allStatuses = Object.keys(statusLabel) as AdmissionStatus[]

export const intakeText = (term: StartTerm, year: number) => `${term} ${year}`

/** A short date from "yyyy-MM-dd" without time-zone shifts. */
export const dayText = (day: string | null | undefined) => (day ? new Date(`${day}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : '—')

export const todayIso = () => new Date().toISOString().slice(0, 10)
