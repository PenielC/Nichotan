import { describe, expect, it } from 'vitest'
import { listMessage, whatsappLink, whatsappNumber } from './whatsapp'

describe('whatsappNumber', () => {
  it('turns an international number into the digits WhatsApp links need', () => {
    expect(whatsappNumber('+263 71 612 4255')).toBe('263716124255')
    expect(whatsappNumber('+1 (416) 555-0123')).toBe('14165550123')
    expect(whatsappNumber('00263716124255')).toBe('263716124255')
  })

  it('does not guess a country for a number typed without one', () => {
    expect(whatsappNumber('0716124255')).toBeNull()
    expect(whatsappNumber('716124255')).toBeNull()
  })

  it('rejects empty and implausible numbers', () => {
    expect(whatsappNumber(null)).toBeNull()
    expect(whatsappNumber('')).toBeNull()
    expect(whatsappNumber('+12')).toBeNull()
    expect(whatsappNumber('+1234567890123456789')).toBeNull()
  })
})

describe('whatsappLink', () => {
  it('opens a chat with the number, with the message safely encoded', () => {
    const url = whatsappLink('263716124255', 'Hi & welcome\nSecond line')
    expect(url).toBe('https://wa.me/263716124255?text=Hi%20%26%20welcome%0ASecond%20line')
  })

  it('lets the staff member choose the contact when there is no usable number', () => {
    expect(whatsappLink(null, 'Hello')).toBe('https://wa.me/?text=Hello')
  })
})

describe('listMessage', () => {
  it('names the sender and the items, with a link, and nothing personal', () => {
    const text = listMessage({ firstName: 'Rudo', staffFirstName: 'Tendai', intro: 'Here is your pre-departure list:', lines: ['Passport with your visa', 'Proof of funds'], link: 'https://web.example/portal/applications' })
    expect(text).toContain("Hi Rudo, it's Tendai from Nichotan.")
    expect(text).toContain('• Passport with your visa\n• Proof of funds')
    expect(text).toContain('https://web.example/portal/applications')
  })
})
