import { describe, expect, it } from 'vitest'
import { applicantLevelOf, normaliseServerErrors, stepOf, toFormState, toRequest, validateApplication, type ApplicationFormData, type ServerFormData } from './application'

const empty = (): ApplicationFormData =>
  toFormState({ personal: {}, contact: {}, program: {}, academic: {} } as unknown as ServerFormData)

/** A form that passes every check, for an undergraduate. */
const complete = (): ApplicationFormData => {
  const d = empty()
  d.personal = { firstName: 'Tanaka', middleNames: '', lastName: 'Moyo', gender: 'Female', birthDate: '2004-05-17', citizenshipCountry: 'Zimbabwe' }
  d.contact = { homeAddress: '1 Test Road', city: 'Harare', country: 'Zimbabwe', email: 'a@b.co', phone: '+263770000000', alternativePhone: '', nextOfKinName: 'Rudo', nextOfKinEmail: 'r@b.co', nextOfKinPhone: '' }
  d.program = { destinationCountry: 'Canada', programLevel: 'Bachelors', startYear: '2030', startTerm: 'Fall', institutions: ['UofT', '', '', '', ''], programChoices: ['CS', ''] }
  d.academic.highSchools[0] = { name: 'Harare High', provinceCountry: 'Harare, Zimbabwe', started: '2016-01', completed: '2021-11', grade: 'TwelveOrEquivalent' }
  return d
}

describe('applicantLevelOf', () => {
  it('puts high school, undergraduate and postgraduate program levels in their own groups', () => {
    expect(applicantLevelOf('HighSchool')).toBe('HighSchool')
    for (const l of ['Bachelors', 'Diploma', 'Certificate'] as const) expect(applicantLevelOf(l)).toBe('Undergraduate')
    for (const l of ['Masters', 'PostGraduateDiplomaOrCertificate'] as const) expect(applicantLevelOf(l)).toBe('Postgraduate')
    expect(applicantLevelOf('')).toBeNull()
  })
})

describe('validateApplication', () => {
  it('accepts a complete undergraduate form', () => {
    expect(validateApplication(complete(), 2026)).toEqual({})
  })

  it('asks for the basics on an empty form', () => {
    const errors = validateApplication(empty(), 2026)
    expect(errors['personal.firstname']).toBeDefined()
    expect(errors['program.destinationcountry']).toBe('Choose Canada or Germany.')
    expect(errors['program.institutions']).toBeDefined()
  })

  it('requires high school for high school and undergraduate applicants, and post-secondary for postgraduates', () => {
    const noSchool = complete()
    noSchool.academic.highSchools[0] = { name: '', provinceCountry: '', started: '', completed: '', grade: '' }
    expect(validateApplication(noSchool, 2026)['academic.highschools']).toBeDefined()
    noSchool.program.programLevel = 'HighSchool'
    expect(validateApplication(noSchool, 2026)['academic.highschools']).toBeDefined()

    noSchool.program.programLevel = 'Masters'
    const errors = validateApplication(noSchool, 2026)
    expect(errors['academic.highschools']).toBeUndefined()
    expect(errors['academic.postsecondary']).toBeDefined()
  })

  it('checks the rows that were started and ignores empty ones', () => {
    const d = complete()
    d.academic.highSchools[1] = { name: 'Second', provinceCountry: '', started: '2022-05', completed: '2021-01', grade: '' }
    const errors = validateApplication(d, 2026)
    expect(errors['academic.highschools.1.provincecountry']).toBeDefined()
    expect(errors['academic.highschools.1.completed']).toBe('This can’t be before the start date.')
    expect(errors['academic.highschools.0.name']).toBeUndefined()
  })

  it('rejects a start year in the past', () => {
    const d = complete()
    d.program.startYear = '2020'
    expect(validateApplication(d, 2026)['program.startyear']).toBeDefined()
  })
})

describe('toRequest', () => {
  it('turns empty text into null and drops empty rows', () => {
    const request = toRequest(complete())
    expect(request.personal.middleNames).toBeNull()
    expect(request.program.institutions).toEqual(['UofT'])
    expect(request.program.startYear).toBe(2030)
    expect(request.academic.highSchools).toHaveLength(1)
    expect(request.academic.postSecondary).toHaveLength(0)
  })
})

describe('server errors', () => {
  it('are keyed like the form fields', () => {
    const keyed = normaliseServerErrors({ 'Academic.HighSchools[1].Name': ['Required'], 'Personal.FirstName': ['x'] })
    expect(keyed['academic.highschools.1.name']).toBe('Required')
    expect(stepOf('academic.highschools.1.name')).toBe('academic')
    expect(stepOf('contact.email')).toBe('personal')
    expect(stepOf('program.startyear')).toBe('program')
  })
})
