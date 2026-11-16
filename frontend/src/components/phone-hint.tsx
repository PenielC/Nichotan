import { CheckCircle2, Info, TriangleAlert } from 'lucide-react'
import { phoneHelp } from '@/lib/phone'

/**
 * The help under a phone number field. It shows examples before anything is typed, then reacts to what is typed (a local number
 * gets told exactly how to fix it). It is guidance, not a block: the form's own check says what is wrong when it is submitted.
 */
export function PhoneHint({ value }: { value: string | null | undefined }) {
  const { tone, text } = phoneHelp(value)
  const Icon = tone === 'ok' ? CheckCircle2 : tone === 'warn' ? TriangleAlert : Info
  return (
    <span className={`flex items-start gap-1.5 ${tone === 'warn' ? 'text-amber-800' : tone === 'ok' ? 'text-primary' : ''}`} aria-live="polite">
      <Icon className="mt-0.5 size-3.5 shrink-0" aria-hidden />
      <span>{text}</span>
    </span>
  )
}
