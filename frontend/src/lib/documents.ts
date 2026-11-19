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
  /** The document is a set of files (academic records), reviewed together. */
  allowsMultipleFiles: boolean
  /** The files in the document now, in the order they were first added. */
  files: SlotFile[]
}

export interface SlotFile {
  position: number
  versionNumber: number
  fileName: string
  sizeBytes: number
  uploadedAtUtc: string
  expiresOn: string | null
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
  versions: { versionNumber: number; fileName: string; contentType: string; sizeBytes: number; uploadedAtUtc: string; uploadedBy: string | null; expiresOn: string | null; position: number; removed: boolean }[]
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
  fileCount: number
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
  allowsMultipleFiles: boolean
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

/** The most files one document can hold. */
export const maxFilesPerDocument = 10

export function formatFileSize(bytes: number): string {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** A slot the student can put a file into: not a letter from the school, not signed in the form, and not already accepted. */
export const canUploadTo = (s: Pick<DocumentSlot, 'isAutomatic' | 'purpose' | 'status'>) => !s.isAutomatic && s.purpose === 'None' && s.status !== 'Accepted'

/** One slot is one document type, and for a school-specific one, one application. */
export const slotKey = (s: Pick<DocumentSlot, 'documentTypeId' | 'applicationId'>) => `${s.documentTypeId}:${s.applicationId ?? ''}`

const fillerWords = new Set(['of', 'the', 'and', 'for', 'my', 'copy', 'scan', 'to', 'a'])
const words = (text: string) => text.toLowerCase().replace(/\.[a-z0-9]+$/, '').split(/[^a-z]+/).filter((w) => w.length > 1 && !fillerWords.has(w))

/**
 * Picks the document a file is probably for, from its name ("my-passport.pdf" is the passport). It only answers when one slot is
 * clearly the best match, and never one already taken, so a wrong guess is rare and the student can always change it.
 */
export function guessSlot(fileName: string, slots: (Pick<DocumentSlot, 'documentTypeId' | 'applicationId' | 'typeName'> & { allowsMultipleFiles?: boolean })[], taken: ReadonlySet<string>): string | null {
  const fileWords = new Set(words(fileName))
  let best: { key: string; ratio: number; hits: number } | null = null
  let tied = false
  for (const s of slots) {
    const key = slotKey(s)
    // A document that holds several files can take more than one, so it is never "taken".
    if (taken.has(key) && !s.allowsMultipleFiles) continue
    const slotWords = words(s.typeName)
    const hits = slotWords.filter((w) => fileWords.has(w)).length
    if (hits === 0) continue
    const ratio = hits / slotWords.length
    if (!best || hits > best.hits || (hits === best.hits && ratio > best.ratio)) {
      best = { key, ratio, hits }
      tied = false
    } else if (ratio === best.ratio && hits === best.hits) tied = true
  }
  return best && !tied ? best.key : null
}
