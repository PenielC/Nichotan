// The Nichotan application form: its shape (mirrors the API), the choices it offers, and the checks run before
import { phoneAdvice, phoneProblem } from '@/lib/phone'
// submitting (mirroring the server's, which still checks everything).

export type Gender = 'Male' | 'Female'
export type DestinationCountry = 'Canada' | 'Germany'
export type ProgramLevel = 'HighSchool' | 'Bachelors' | 'Diploma' | 'Certificate' | 'Masters' | 'PostGraduateDiplomaOrCertificate'
export type ApplicantLevel = 'HighSchool' | 'Undergraduate' | 'Postgraduate'
export type StartTerm = 'Winter' | 'Summer' | 'Fall'
export type HighSchoolGrade = 'LessThan12' | 'TwelveOrEquivalent'
export type FormStatus = 'Draft' | 'Submitted' | 'Reopened'

export interface HighSchoolEntry {
  name: string
  provinceCountry: string
  started: string
  completed: string
  grade: HighSchoolGrade | ''
}

export interface PostSecondaryEntry {
  institution: string
  provinceCountry: string
  started: string
  completed: string
  credentialAwarded: string
  credentialAwardedOn: string
}

export interface ApplicationFormData {
  personal: { firstName: string; middleNames: string; lastName: string; gender: Gender | ''; birthDate: string; citizenshipCountry: string }
  contact: {
    homeAddress: string
    city: string
    country: string
    email: string
    phone: string
    alternativePhone: string
    nextOfKinName: string
    nextOfKinEmail: string
    nextOfKinPhone: string
  }
  program: { destinationCountry: DestinationCountry | ''; programLevel: ProgramLevel | ''; startYear: string; startTerm: StartTerm | ''; institutions: string[]; programChoices: string[] }
  academic: { highSchools: HighSchoolEntry[]; postSecondary: PostSecondaryEntry[] }
}

export interface FormVersionSummary {
  versionNumber: number
  submittedAtUtc: string
  signerName: string
  contentHash: string
}

export interface ApplicationFormResult {
  id: string | null
  candidateId: string
  status: FormStatus
  isEditable: boolean
  data: ServerFormData
  applicantLevel: ApplicantLevel | null
  currentVersion: number
  submittedAtUtc: string | null
  reopenReason: string | null
  reopenedAtUtc: string | null
  declarationVersion: string
  declarationText: string
  versions: FormVersionSummary[]
}

export interface FormVersionResult {
  candidateId: string
  versionNumber: number
  data: ServerFormData
  applicantLevel: ApplicantLevel | null
  submittedAtUtc: string
  contentHash: string
  signature: { provider: string; signerName: string; signedAtUtc: string; ipAddress: string | null; imageDataUrl: string | null; declarationVersion: string; declarationText: string }
}

export interface ApplicationFormSummary {
  candidateId: string
  candidateName: string
  email: string
  status: FormStatus
  currentVersion: number
  destinationCountry: DestinationCountry | null
  applicantLevel: ApplicantLevel | null
  submittedAtUtc: string | null
  lastChangedAtUtc: string
}

/** What the API sends: any field may be null or missing (the year is a number, not text). */
type DeepNullable<T> = T extends (infer U)[] ? DeepNullable<U>[] : T extends object ? { [K in keyof T]: DeepNullable<T[K]> } : T | null
export type ServerFormData = DeepNullable<ApplicationFormData>

/** The cache key for the signed-in candidate's own form. */
export const applicationKey = ['application-form'] as const

export const maxInstitutions = 5
export const maxProgramChoices = 2
export const maxHighSchools = 2
export const maxPostSecondary = 3

export const genders: { value: Gender; label: string }[] = [
  { value: 'Female', label: 'Female' },
  { value: 'Male', label: 'Male' },
]

export const destinations: DestinationCountry[] = ['Canada', 'Germany']

export const programLevels: { value: ProgramLevel; label: string }[] = [
  { value: 'HighSchool', label: 'High school' },
  { value: 'Bachelors', label: 'Bachelor’s degree' },
  { value: 'Diploma', label: 'Diploma' },
  { value: 'Certificate', label: 'Certificate' },
  { value: 'Masters', label: 'Graduate degree (Master’s)' },
  { value: 'PostGraduateDiplomaOrCertificate', label: 'Post Graduate Diploma/Certificate' },
]

export const startTerms: { value: StartTerm; label: string }[] = [
  { value: 'Winter', label: 'Winter (Jan–Apr)' },
  { value: 'Summer', label: 'Summer (May–Aug)' },
  { value: 'Fall', label: 'Fall (Sep–Dec)' },
]

export const highSchoolGrades: { value: HighSchoolGrade; label: string }[] = [
  { value: 'LessThan12', label: 'Less than 12' },
  { value: 'TwelveOrEquivalent', label: '12 or equivalent (A level)' },
]

export const levelLabel = (level: string | null | undefined) => programLevels.find((l) => l.value === level)?.label ?? '—'

export const applicantLevelLabel: Record<ApplicantLevel, string> = { HighSchool: 'High school', Undergraduate: 'Undergraduate', Postgraduate: 'Postgraduate' }

export const statusLabel: Record<FormStatus, string> = { Draft: 'Draft', Submitted: 'Submitted', Reopened: 'Needs changes' }

/** Document requirements differ by level; high school follows the undergraduate flow. */
export function applicantLevelOf(level: ProgramLevel | '' | null | undefined): ApplicantLevel | null {
  if (!level) return null
  if (level === 'HighSchool') return 'HighSchool'
  return level === 'Masters' || level === 'PostGraduateDiplomaOrCertificate' ? 'Postgraduate' : 'Undergraduate'
}

// ─────────────────────────── Moving between the API and the screen ───────────────────────────

const blankHighSchool = (): HighSchoolEntry => ({ name: '', provinceCountry: '', started: '', completed: '', grade: '' })
const blankPostSecondary = (): PostSecondaryEntry => ({ institution: '', provinceCountry: '', started: '', completed: '', credentialAwarded: '', credentialAwardedOn: '' })

const pad = <T>(items: T[], size: number, blank: () => T): T[] => [...items.slice(0, size), ...Array.from({ length: Math.max(0, size - items.length) }, blank)]
const text = (v: string | null | undefined) => v ?? ''

/** Server data → what the form edits: no nulls, and a fixed number of rows for each list, as on the paper form. */
export function toFormState(d: ServerFormData): ApplicationFormData {
  return {
    personal: {
      firstName: text(d.personal.firstName),
      middleNames: text(d.personal.middleNames),
      lastName: text(d.personal.lastName),
      gender: (d.personal.gender ?? '') as Gender | '',
      birthDate: text(d.personal.birthDate),
      citizenshipCountry: text(d.personal.citizenshipCountry),
    },
    contact: {
      homeAddress: text(d.contact.homeAddress),
      city: text(d.contact.city),
      country: text(d.contact.country),
      email: text(d.contact.email),
      phone: text(d.contact.phone),
      alternativePhone: text(d.contact.alternativePhone),
      nextOfKinName: text(d.contact.nextOfKinName),
      nextOfKinEmail: text(d.contact.nextOfKinEmail),
      nextOfKinPhone: text(d.contact.nextOfKinPhone),
    },
    program: {
      destinationCountry: (d.program.destinationCountry ?? '') as DestinationCountry | '',
      programLevel: (d.program.programLevel ?? '') as ProgramLevel | '',
      startYear: d.program.startYear == null ? '' : String(d.program.startYear),
      startTerm: (d.program.startTerm ?? '') as StartTerm | '',
      institutions: pad((d.program.institutions ?? []).map(text), maxInstitutions, () => ''),
      programChoices: pad((d.program.programChoices ?? []).map(text), maxProgramChoices, () => ''),
    },
    academic: {
      highSchools: pad(
        (d.academic.highSchools ?? []).map((h) => ({
          name: text(h.name),
          provinceCountry: text(h.provinceCountry),
          started: text(h.started),
          completed: text(h.completed),
          grade: (h.grade ?? '') as HighSchoolGrade | '',
        })),
        maxHighSchools,
        blankHighSchool,
      ),
      postSecondary: pad(
        (d.academic.postSecondary ?? []).map((p) => ({
          institution: text(p.institution),
          provinceCountry: text(p.provinceCountry),
          started: text(p.started),
          completed: text(p.completed),
          credentialAwarded: text(p.credentialAwarded),
          credentialAwardedOn: text(p.credentialAwardedOn),
        })),
        maxPostSecondary,
        blankPostSecondary,
      ),
    },
  }
}

const orNull = (v: string) => (v.trim() === '' ? null : v.trim())
const isBlank = (o: object) => Object.values(o).every((v) => String(v).trim() === '')

/** What the form edits → the API: empty text becomes null, and rows left empty are dropped. */
export function toRequest(d: ApplicationFormData) {
  return {
    personal: {
      firstName: orNull(d.personal.firstName),
      middleNames: orNull(d.personal.middleNames),
      lastName: orNull(d.personal.lastName),
      gender: d.personal.gender || null,
      birthDate: orNull(d.personal.birthDate),
      citizenshipCountry: orNull(d.personal.citizenshipCountry),
    },
    contact: Object.fromEntries(Object.entries(d.contact).map(([k, v]) => [k, orNull(v)])),
    program: {
      destinationCountry: d.program.destinationCountry || null,
      programLevel: d.program.programLevel || null,
      startYear: d.program.startYear.trim() === '' ? null : Number(d.program.startYear),
      startTerm: d.program.startTerm || null,
      institutions: d.program.institutions.map((v) => v.trim()).filter(Boolean),
      programChoices: d.program.programChoices.map((v) => v.trim()).filter(Boolean),
    },
    academic: {
      highSchools: d.academic.highSchools
        .filter((h) => !isBlank(h))
        .map((h) => ({ name: orNull(h.name), provinceCountry: orNull(h.provinceCountry), started: orNull(h.started), completed: orNull(h.completed), grade: h.grade || null })),
      postSecondary: d.academic.postSecondary
        .filter((p) => !isBlank(p))
        .map((p) => ({
          institution: orNull(p.institution),
          provinceCountry: orNull(p.provinceCountry),
          started: orNull(p.started),
          completed: orNull(p.completed),
          credentialAwarded: orNull(p.credentialAwarded),
          credentialAwardedOn: orNull(p.credentialAwardedOn),
        })),
    },
  }
}

// ─────────────────────────── Checks before submitting ───────────────────────────

export type FormStep = 'personal' | 'program' | 'academic' | 'sign'

/** Error messages keyed by lower-case field path, e.g. "personal.firstname" or "academic.highschools.0.name". */
export type FormErrors = Record<string, string>

const required = 'This is required.'
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const yearMonth = /^(19|20)\d{2}-(0[1-9]|1[0-2])$/

export function validateApplication(d: ApplicationFormData, thisYear = new Date().getFullYear()): FormErrors {
  const e: FormErrors = {}
  const need = (path: string, value: string, message = required) => {
    if (value.trim() === '') e[path] = message
  }

  need('personal.firstname', d.personal.firstName)
  need('personal.lastname', d.personal.lastName)
  need('personal.gender', d.personal.gender)
  need('personal.birthdate', d.personal.birthDate, 'Enter your date of birth.')
  if (d.personal.birthDate && new Date(d.personal.birthDate) > new Date()) e['personal.birthdate'] = 'Enter a valid date of birth.'
  need('personal.citizenshipcountry', d.personal.citizenshipCountry)

  need('contact.homeaddress', d.contact.homeAddress)
  need('contact.city', d.contact.city)
  need('contact.country', d.contact.country)
  if (!emailPattern.test(d.contact.email.trim())) e['contact.email'] = 'Enter a valid email address.'
  need('contact.phone', d.contact.phone)
  if (d.contact.phone.trim() && phoneProblem(d.contact.phone)) e['contact.phone'] = phoneAdvice
  if (phoneProblem(d.contact.alternativePhone)) e['contact.alternativephone'] = phoneAdvice
  if (phoneProblem(d.contact.nextOfKinPhone)) e['contact.nextofkinphone'] = phoneAdvice
  need('contact.nextofkinname', d.contact.nextOfKinName)
  if (!emailPattern.test(d.contact.nextOfKinEmail.trim())) e['contact.nextofkinemail'] = 'Enter a valid email address.'

  need('program.destinationcountry', d.program.destinationCountry, 'Choose Canada or Germany.')
  need('program.programlevel', d.program.programLevel, 'Choose your program level.')
  const year = Number(d.program.startYear)
  if (!d.program.startYear.trim() || !Number.isInteger(year) || year < thisYear || year > thisYear + 10) e['program.startyear'] = 'Enter the year you want to start.'
  need('program.startterm', d.program.startTerm, 'Choose when you want to start.')
  if (!d.program.institutions.some((v) => v.trim())) e['program.institutions'] = 'Name at least one institution.'
  if (!d.program.programChoices.some((v) => v.trim())) e['program.programchoices'] = 'Name at least one programme.'

  const level = applicantLevelOf(d.program.programLevel)
  const schools = d.academic.highSchools.filter((h) => !isBlank(h))
  const colleges = d.academic.postSecondary.filter((p) => !isBlank(p))
  if (level && level !== 'Postgraduate' && schools.length === 0) e['academic.highschools'] = 'Add at least one high school.'
  if (level === 'Postgraduate' && colleges.length === 0) e['academic.postsecondary'] = 'Add at least one post-secondary institution.'

  const dates = (prefix: string, row: { started: string; completed: string }) => {
    if (!yearMonth.test(row.started)) e[`${prefix}.started`] = 'Choose the month you started.'
    if (row.completed && !yearMonth.test(row.completed)) e[`${prefix}.completed`] = 'Choose a valid month.'
    else if (row.completed && row.started && row.completed < row.started) e[`${prefix}.completed`] = 'This can’t be before the start date.'
  }
  d.academic.highSchools.forEach((h, i) => {
    if (isBlank(h)) return
    const p = `academic.highschools.${i}`
    need(`${p}.name`, h.name)
    need(`${p}.provincecountry`, h.provinceCountry)
    need(`${p}.grade`, h.grade)
    dates(p, h)
  })
  d.academic.postSecondary.forEach((c, i) => {
    if (isBlank(c)) return
    const p = `academic.postsecondary.${i}`
    need(`${p}.institution`, c.institution)
    need(`${p}.provincecountry`, c.provinceCountry)
    dates(p, c)
  })
  return e
}

export function stepOf(path: string): FormStep {
  if (path.startsWith('program')) return 'program'
  if (path.startsWith('academic')) return 'academic'
  if (path.startsWith('personal') || path.startsWith('contact')) return 'personal'
  return 'sign'
}

/** The server names fields like "Academic.HighSchools[0].Name"; the form keys them like "academic.highschools.0.name". */
export function normaliseServerErrors(errors: Record<string, string[]>): FormErrors {
  const out: FormErrors = {}
  for (const [key, messages] of Object.entries(errors)) out[key.replace(/\[(\d+)\]/g, '.$1').replace(/^data\./i, '').toLowerCase()] = messages[0]
  return out
}
