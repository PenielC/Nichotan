import { describe, expect, it } from 'vitest'
import { normalisePhone, phoneHelp, phoneProblem } from './phone'

describe('normalisePhone', () => {
  it('keeps only + and the digits, turning 00 into +', () => {
    expect(normalisePhone('+263 77 123 4567')).toBe('+263771234567')
    expect(normalisePhone('00263 77 123 4567')).toBe('+263771234567')
    expect(normalisePhone(' +1 (416) 555-0123 ')).toBe('+14165550123')
  })

  it('refuses a number with no country code or of an impossible length', () => {
    for (const bad of ['077 123 4567', '0771234567', '771234567', '+12', '+0771234567', '+1234567890123456', 'call me', '', null, undefined]) {
      expect(normalisePhone(bad)).toBeNull()
    }
  })
})

describe('phoneProblem', () => {
  it('lets an empty optional number through and explains a local one', () => {
    expect(phoneProblem('')).toBeNull()
    expect(phoneProblem('+263 77 123 4567')).toBeNull()
    expect(phoneProblem('077 123 4567')).toContain('country code')
  })
})

describe('phoneHelp', () => {
  it('shows examples first', () => {
    const help = phoneHelp('')
    expect(help.tone).toBe('info')
    expect(help.text).toContain('+263')
    expect(help.text).toContain('+1 416')
  })

  it('tells someone with a local number exactly how to fix it', () => {
    const help = phoneHelp('0771234567')
    expect(help.tone).toBe('warn')
    expect(help.text).toContain('Replace the first 0')
  })

  it('asks for the + when it is missing, and for more digits when it is short', () => {
    expect(phoneHelp('263771234567').text).toContain('starting with +')
    expect(phoneHelp('+26').text).toContain('full number')
  })

  it('says well done for a good number', () => {
    expect(phoneHelp('+263 77 123 4567').tone).toBe('ok')
  })
})
