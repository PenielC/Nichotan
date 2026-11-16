/**
 * Phone numbers are kept with the country code first (+ and digits) so staff can reach a student by call or WhatsApp from anywhere.
 * A local number such as 077 123 4567 can't be used that way, and the country can't be guessed from it. This mirrors the server's rule.
 */

/** The same message the server gives, so a refusal reads the same wherever it comes from. */
export const phoneAdvice = 'Start the phone number with your country code, for example +263 77 123 4567. A local number like 077 123 4567 can’t be reached on WhatsApp.'

/** "+263 77 123 4567" and "00263 77 123 4567" both become "+263771234567". Null if it doesn't start with + or 00, or isn't a plausible length. */
export function normalisePhone(raw: string | null | undefined): string | null {
  if (!raw) return null
  const text = raw.trim()
  if (!text.startsWith('+') && !text.startsWith('00')) return null
  let digits = text.replace(/\D/g, '')
  if (text.startsWith('00')) digits = digits.slice(2)
  // 8 to 15 digits is the range real international numbers fall in, and no country code starts with 0.
  if (digits.length < 8 || digits.length > 15 || digits.startsWith('0')) return null
  return `+${digits}`
}

/** For optional fields: an empty value is fine, anything typed must be in international form. */
export const phoneProblem = (value: string | null | undefined): string | null => (!value || !value.trim() || normalisePhone(value) ? null : phoneAdvice)

export type PhoneHelp = { tone: 'info' | 'warn' | 'ok'; text: string }

/** What to say under the field as the person types: examples first, then a nudge that fits what they've typed so far. */
export function phoneHelp(value: string | null | undefined): PhoneHelp {
  const text = (value ?? '').trim()
  if (!text) return { tone: 'info', text: 'Start with + and your country code, for example +263 77 123 4567 (Zimbabwe), +1 416 555 0123 (Canada) or +49 151 2345 6789 (Germany).' }
  if (normalisePhone(text)) return { tone: 'ok', text: 'Looks good. We can reach you on this number, including on WhatsApp.' }
  if (text.startsWith('0') && !text.startsWith('00')) return { tone: 'warn', text: 'That looks like a local number. Replace the first 0 with your country code, so 077 123 4567 becomes +263 77 123 4567.' }
  if (!text.startsWith('+') && !text.startsWith('00')) return { tone: 'warn', text: 'Put your country code first, starting with +, for example +263 or +1.' }
  return { tone: 'warn', text: 'That doesn’t look like a full number yet. Check the digits after the country code.' }
}
