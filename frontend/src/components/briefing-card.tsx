import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CalendarCheck } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { get } from '@/lib/api'
import { urgencyLabel, type Briefing, type BriefingUrgency } from '@/lib/briefing'

const badgeVariant: Record<BriefingUrgency, 'destructive' | 'default' | 'secondary'> = { Overdue: 'destructive', Today: 'default', Soon: 'secondary' }

/**
 * "Today": the short list a staff member should look at first. It is worked out from fixed rules (their own tasks, plus overdue
 * biometrics and PAL follow-ups that everyone shares), so it is the same every time for the same data.
 */
export function BriefingCard() {
  const briefing = useQuery({ queryKey: ['briefing'], queryFn: () => get<Briefing>('/api/briefing'), refetchInterval: 60_000 })

  return (
    <Card className="mb-6 border-l-4 border-l-primary">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-ink">
          <CalendarCheck className="size-5 text-primary" aria-hidden /> Today
        </CardTitle>
        <CardDescription>{briefing.data ? briefing.data.headline : 'Checking what needs your attention…'}</CardDescription>
      </CardHeader>
      <CardContent>
        {briefing.isLoading ? (
          <Skeleton className="h-20" />
        ) : briefing.isError ? (
          <p className="text-sm text-destructive">We couldn’t load today’s list. Please refresh the page.</p>
        ) : briefing.data?.items.length ? (
          <ul className="divide-y">
            {briefing.data.items.map((i) => (
              <li key={`${i.kind}:${i.path}:${i.title}`}>
                <Link to={i.path} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                  <span className="min-w-0 text-sm">
                    <span className="font-medium text-ink">{i.title}</span>
                    <span className="block text-xs text-muted-foreground">{i.detail}</span>
                  </span>
                  <Badge variant={badgeVariant[i.urgency]}>{urgencyLabel[i.urgency]}</Badge>
                </Link>
              </li>
            ))}
          </ul>
        ) : (
          <p className="py-2 text-sm text-muted-foreground">You’re all caught up.</p>
        )}
      </CardContent>
    </Card>
  )
}
