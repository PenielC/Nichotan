import { BadgeCheck, FileText, Fingerprint, Landmark, Mail, Plane, Stamp, type LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

/** The seven stages of the Nichotan student journey, as shown to candidates. */
const journeySteps: { title: string; icon: LucideIcon }[] = [
  { title: 'Admission application', icon: FileText },
  { title: 'Offer letter or rejection', icon: Mail },
  { title: 'Acceptance', icon: Landmark },
  { title: 'Visa process', icon: Stamp },
  { title: 'Biometrics', icon: Fingerprint },
  { title: 'Visa approved', icon: BadgeCheck },
  { title: 'Pre-departure', icon: Plane },
]

/** The journey as a row of numbered stages. `current` is the stage in progress (0-based), or null before anything starts. */
export function Journey({ current, className }: { current: number | null; className?: string }) {
  return (
    <ol className={cn('grid gap-3 sm:grid-cols-7 sm:gap-2', className)}>
      {journeySteps.map(({ title, icon: Icon }, index) => {
        const done = current !== null && index < current
        const active = current === index
        return (
          <li key={title} className="flex items-center gap-3 sm:flex-col sm:text-center" aria-current={active ? 'step' : undefined}>
            <span
              className={cn(
                'flex size-11 shrink-0 items-center justify-center rounded-full border-2',
                done && 'border-primary bg-primary text-primary-foreground',
                active && 'border-primary bg-accent text-primary',
                !done && !active && 'border-border bg-card text-muted-foreground',
              )}
            >
              <Icon className="size-5" aria-hidden />
            </span>
            <span className={cn('text-sm sm:text-xs', active ? 'font-semibold text-ink' : 'text-muted-foreground')}>
              {index + 1}. {title}
              <span className="sr-only">{done ? ' (complete)' : active ? ' (in progress)' : ' (not started)'}</span>
            </span>
          </li>
        )
      })}
    </ol>
  )
}
