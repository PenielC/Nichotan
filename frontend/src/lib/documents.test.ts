import { describe, expect, it } from 'vitest'
import { canUploadTo, guessSlot, slotKey } from './documents'

const slot = (typeName: string, applicationId: string | null = null) => ({ documentTypeId: typeName.toLowerCase().replace(/\W+/g, '-'), applicationId, typeName })
const slots = [slot('Passport'), slot('Passport-style photo'), slot('Proof of funds'), slot('Police clearance certificate'), slot('Study plan')]

describe('guessSlot', () => {
  it('matches a file to the document its name describes', () => {
    expect(guessSlot('my-passport.pdf', slots, new Set())).toBe(slotKey(slots[0]))
    expect(guessSlot('Passport photo.JPG', slots, new Set())).toBe(slotKey(slots[1]))
    expect(guessSlot('proof_of_funds_bank.png', slots, new Set())).toBe(slotKey(slots[2]))
    expect(guessSlot('police clearance.pdf', slots, new Set())).toBe(slotKey(slots[3]))
  })

  it('does not guess when nothing matches, when two documents match equally, or when the match is already taken', () => {
    expect(guessSlot('IMG_20261104_1234.jpg', slots, new Set())).toBeNull()
    const twoApplications = [slot('Offer', 'a1'), slot('Offer', 'a2')]
    expect(guessSlot('offer.pdf', twoApplications, new Set())).toBeNull()
    expect(guessSlot('passport.pdf', slots, new Set([slotKey(slots[0])]))).toBe(slotKey(slots[1]))
  })
})

describe('guessSlot with a document that holds several files', () => {
  const records = { ...slot('Academic records'), allowsMultipleFiles: true }
  const list = [slot('Passport'), records]

  it('keeps matching every file to it instead of treating it as taken', () => {
    const taken = new Set<string>()
    for (const name of ['academic-records-grade-11.pdf', 'academic records grade 12.pdf']) {
      const key = guessSlot(name, list, taken)
      expect(key).toBe(slotKey(records))
      if (key && !records.allowsMultipleFiles) taken.add(key)
    }
    expect(guessSlot('academic-records.pdf', list, new Set([slotKey(records)]))).toBe(slotKey(records))
  })
})

describe('canUploadTo', () => {
  it('leaves out the school’s letters, the form and anything already accepted', () => {
    expect(canUploadTo({ isAutomatic: false, purpose: 'None', status: 'Missing' })).toBe(true)
    expect(canUploadTo({ isAutomatic: false, purpose: 'None', status: 'NeedsResubmission' })).toBe(true)
    expect(canUploadTo({ isAutomatic: true, purpose: 'None', status: 'Missing' })).toBe(false)
    expect(canUploadTo({ isAutomatic: false, purpose: 'OfferLetter', status: 'Missing' })).toBe(false)
    expect(canUploadTo({ isAutomatic: false, purpose: 'None', status: 'Accepted' })).toBe(false)
  })
})
