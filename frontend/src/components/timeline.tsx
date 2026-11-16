import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { FileText, GraduationCap, ListChecks, Stamp, UserPlus, FileSignature, type LucideIcon } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { get } from '@/lib/api'
import { dayText } from '@/lib/admissions'
import { formatDateTime } from '@/lib/format'
import type { TimelineEntry } from '@/lib/tasks'

const icon: Record<TimelineEntry['kind'], LucideIcon> = { Account: UserPlus, Form: FileSignature, Admission: GraduationCap, Visa: Stamp, Document: FileText, Task: ListChecks }

const linkOf = (e: TimelineEntry) => (e.linkType === 'admission' && e.linkId ? `/staff/admissions/${e.linkId}` : e.linkType === 'visa' && e.linkId ? `/staff/visa/${e.linkId}` : null)

/** Everything that has happened on a student's record, newest first. Opening it is audited, so it loads only when asked for. */
export function TimelineCard({ candidateId }: { candidateId: string }) {
  const [open, setOpen] = useState(false)
  const timeline = useQuery({ queryKey: ['timeline', candidateId], queryFn: () => get<TimelineEntry[]>(`/api/candidates/${candidateId}/timeline`), enabled: open, refetchOnWindowFocus: false })
  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
        <div>
          <CardTitle className="text-lg text-ink">Timeline</CardTitle>
          <CardDescription>Everything that has happened on this record, newest first: moves, documents, tasks and more.</CardDescription>
        </div>
        {!open && (
          <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
            Show the timeline
          </Button>
        )}
      </CardHeader>
      {open && (
        <CardContent>
          {!timeline.data ? (
            <Skeleton className="h-32" />
          ) : (
            <ol className="grid gap-4">
              {timeline.data.map((e, i) => {
                const Icon = icon[e.kind]
                const to = linkOf(e)
                return (
                  <li key={i} className="flex gap-3">
                    <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full bg-accent text-primary">
                      <Icon className="size-4" aria-hidden />
                    </span>
                    <div className="min-w-0">
                      <p className="text-sm font-medium text-ink">
                        {to ? (
                          <Link to={to} className="hover:underline">
                            {e.title}
                          </Link>
                        ) : (
                          e.title
                        )}
                      </p>
                      <p className="text-xs text-muted-foreground">
                        {formatDateTime(e.atUtc)}
                        {e.by ? ` · ${e.by}` : ''}
                        {e.occurredOn && e.occurredOn !== e.atUtc.slice(0, 10) && <Badge variant="outline" className="ml-2">Happened {dayText(e.occurredOn)}</Badge>}
                      </p>
                      {e.detail && <p className="mt-0.5 text-sm whitespace-pre-wrap text-ink">{e.detail}</p>}
                    </div>
                  </li>
                )
              })}
            </ol>
          )}
        </CardContent>
      )}
    </Card>
  )
}
