import crest from '@/assets/logo-crest.png'
import { cn } from '@/lib/utils'

/** The crest is drawn in white and lime, so it always sits on a navy tile. */
export function Crest({ className }: { className?: string }) {
  return (
    <span className={cn('inline-flex size-12 shrink-0 items-center justify-center rounded-xl bg-ink p-1.5 shadow-sm', className)}>
      <img src={crest} alt="" className="h-full w-auto" />
    </span>
  )
}

/** "NICHO TAN / CONSULTING". `onDark` for navy backgrounds. */
export function Wordmark({ onDark = false, tagline = false, className }: { onDark?: boolean; tagline?: boolean; className?: string }) {
  return (
    <span className={cn('flex flex-col leading-none', className)}>
      <span className={cn('text-[1.05em] font-bold tracking-wide', onDark ? 'text-white' : 'text-ink')}>NICHO TAN</span>
      <span className={cn('text-[1.05em] font-bold tracking-wide', onDark ? 'text-leaf' : 'text-primary')}>CONSULTING</span>
      {tagline && <span className={cn('mt-1 text-[0.6em] font-medium', onDark ? 'text-white/70' : 'text-muted-foreground')}>Your Global Education Partner</span>}
    </span>
  )
}

export function Logo({ onDark = false, tagline = false, className }: { onDark?: boolean; tagline?: boolean; className?: string }) {
  return (
    <span className={cn('inline-flex items-center gap-3 text-base', className)}>
      <Crest />
      <Wordmark onDark={onDark} tagline={tagline} />
    </span>
  )
}
