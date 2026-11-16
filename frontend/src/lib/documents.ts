import type { ApplicantLevel, DestinationCountry } from './application'

export type SlotStatus = 'Missing' | 'Uploaded' | 'UnderReview' | 'Accepted' | 'NeedsResubmission'
export type DocumentStatus = 'Uploaded' | 'UnderReview' | 'Accepted' | 'NeedsResubmission'
export type DocumentScope = 'Candidate' | 'Application'
/** Anything but None is managed by the system (the school's letters). */
export type DocumentPurpose = 'None' | 'OfferLetter' | 'AcceptanceLetter' | 'PalLetter' | 'BiometricsLetter' | 'DecisionLetter'

export interface DocumentSlot {
  documentTypeId: string
  typeName: string
  description: string | null
  scope: DocumentScope
  tracksExpiry: boolean
  isAutomatic: boolean
  applicationId: string | null
  applicationLabel: string | null
  required: boolean
  status: SlotStatus
  documentId: string | null
  version: number
  fileName: string | null
  uploadedAtUtc: string | null
  expiresOn: string | null
  reviewNote: string | null
  purpose: DocumentPurpose
  /** False for visa documents: they are only collected here, the visa platform reviews them. */
  needsReview: boolean
}

export interface DocumentsOverview {
  candidateId: string
  slots: DocumentSlot[]
}

export interface DocumentGaps {
  missing: number
  notAccepted: number
  missingNames: string[]
}

export interface ChecklistLine {
  slot: DocumentSlot
  source: 'Template' | 'Manual'
  note: string | null
}

export interface ChecklistResult {
  applicationId: string
  items: ChecklistLine[]
  gaps: DocumentGaps
}

export interface DocumentDetail {
  id: string
  candidateId: string
  documentTypeId: string
  typeName: string
  applicationId: string | null
  status: DocumentStatus
  currentVersion: number
  versions: { versionNumber: number; fileName: string; contentType: string; sizeBytes: number; uploadedAtUtc: string; uploadedBy: string | null; expiresOn: string | null }[]
  reviews: { versionNumber: number; decision: DocumentStatus; note: string | null; reviewer: string | null; reviewedAtUtc: string }[]
}

export interface ReviewQueueItem {
  documentId: string
  candidateId: string
  candidateName: string
  typeName: string
  status: DocumentStatus
  version: number
  fileName: string
  uploadedAtUtc: string
}

export interface DocumentTypeDto {
  id: string
  name: string
  description: string | null
  scope: DocumentScope
  tracksExpiry: boolean
  isAutomatic: boolean
  isActive: boolean
  sortOrder: number
  purpose: DocumentPurpose
}

/** Which part of the journey a checklist is for. */
export type ChecklistStage = 'Admission' | 'Visa'

export interface Requirement {
  id: string
  stage: ChecklistStage
  documentTypeId: string
  documentTypeName: string
  country: DestinationCountry | null
  level: ApplicantLevel | null
  institutionId: string | null
  institutionName: string | null
  isRequired: boolean
  isActive: boolean
}

export const slotLabel: Record<SlotStatus, string> = {
  Missing: 'Missing',
  Uploaded: 'Uploaded',
  UnderReview: 'Under review',
  Accepted: 'Accepted',
  NeedsResubmission: 'Upload again',
}

/** In the candidate's words. */
export const candidateSlotLabel: Record<SlotStatus, string> = {
  Missing: 'Not uploaded yet',
  Uploaded: 'Uploaded, waiting for review',
  UnderReview: 'Being reviewed',
  Accepted: 'Accepted',
  NeedsResubmission: 'Please upload again',
}

export const acceptedTypes = '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png'
export const maxUploadBytes = 10 * 1024 * 1024
