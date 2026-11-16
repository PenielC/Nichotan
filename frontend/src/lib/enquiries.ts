export type Qualification = 'SecondarySchool' | 'AdvancedLevel' | 'Certificate' | 'Diploma' | 'Bachelors' | 'Masters' | 'Doctorate' | 'Other'
export type DestinationInterest = 'Both' | 'CanadaOnly' | 'GermanyOnly' | 'Other'
export type EnquiryStatus = 'New' | 'Contacted' | 'Closed'

export interface Enquiry {
  id: string
  name: string
  email: string | null
  phone: string | null
  highestQualification: Qualification
  graduationYear: number
  preferredProgramme: string | null
  openToGuidance: boolean
  destinationInterest: DestinationInterest
  message: string | null
  status: EnquiryStatus
  staffNote: string | null
  createdAtUtc: string
  statusChangedAtUtc: string | null
  handledBy: string | null
  /** When an administrator or counsellor first opened it; null while nobody has. */
  viewedAtUtc: string | null
  viewedBy: string | null
}

export interface EnquirySummary {
  unviewed: number
  new: number
  latest: Enquiry[]
}

export const contact = {
  phoneDisplay: '+263 71 612 4255',
  phoneLink: 'tel:+263716124255',
  email: 'enquiries@nichotan.com',
  emailLink: 'mailto:enquiries@nichotan.com',
}

export const qualifications: { value: Qualification; label: string }[] = [
  { value: 'SecondarySchool', label: 'Secondary school (O-level or equivalent)' },
  { value: 'AdvancedLevel', label: 'A-level or equivalent' },
  { value: 'Certificate', label: 'Certificate' },
  { value: 'Diploma', label: 'Diploma' },
  { value: 'Bachelors', label: 'Bachelor’s degree' },
  { value: 'Masters', label: 'Master’s degree' },
  { value: 'Doctorate', label: 'Doctorate' },
  { value: 'Other', label: 'Other' },
]

export const qualificationLabel = (q: Qualification) => qualifications.find((x) => x.value === q)?.label ?? q

export const destinationOptions: { value: DestinationInterest; label: string }[] = [
  { value: 'Both', label: 'Yes, Canada or Germany' },
  { value: 'CanadaOnly', label: 'Canada only' },
  { value: 'GermanyOnly', label: 'Germany only' },
  { value: 'Other', label: 'No, I have another country in mind' },
]

export const destinationLabel = (d: DestinationInterest) => destinationOptions.find((x) => x.value === d)?.label ?? d

export const enquiryStatusLabel: Record<EnquiryStatus, string> = { New: 'New', Contacted: 'Contacted', Closed: 'Closed' }
