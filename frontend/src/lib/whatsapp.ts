import { normalisePhone } from '@/lib/phone'

/**
 * Sharing a list with a student by WhatsApp, with no WhatsApp account or provider set up: the staff member's own WhatsApp opens
 * with the message already written, and they press send. Nothing is sent from the server, so this is free.
 */

/**
 * The student's number in the form WhatsApp's links need: digits only, country code first, no "+" and no leading zeros.
 * A number typed without a country code (such as 0771234567) can't be guessed, so it returns null and the staff member
 * picks the contact in WhatsApp themselves.
 */
export function whatsappNumber(raw: string | null | undefined): string | null {
  return normalisePhone(raw)?.slice(1) ?? null
}

/** The link that opens WhatsApp. With a number it opens that chat; without one WhatsApp asks who to send it to. */
export function whatsappLink(number: string | null, text: string): string {
  return `https://wa.me/${number ?? ''}?text=${encodeURIComponent(text)}`
}

/**
 * A short, plain message: who it's from, what the list is, the items by name, and a link to sign in and see the detail. Only the
 * names of the items go in the message; anything personal stays behind the sign-in on the portal.
 */
export function listMessage({ firstName, staffFirstName, intro, lines, link }: { firstName: string; staffFirstName: string; intro: string; lines: string[]; link: string }): string {
  const items = lines.map((l) => `• ${l}`).join('\n')
  return `Hi ${firstName}, it's ${staffFirstName} from Nichotan. ${intro}\n\n${items}\n\nSee the details and update it here: ${link}\n\nReply here if you have any questions.`
}
