import { Fragment, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronLeft, ChevronRight } from 'lucide-react'
import { FormError } from '@/components/form'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { PageHeader } from '@/components/page-header'
import { get, query, type AuditEvent, type Paged } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

const pageSize = 50
const all = 'all'

/** "CandidateProfileViewed" → "Candidate profile viewed" */
const humanise = (name: string) => name.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ (\w)/g, (m) => m.toLowerCase())

const outcomeStyle: Record<AuditEvent['outcome'], string> = {
  Success: 'bg-accent text-accent-foreground',
  Failure: 'bg-amber-100 text-amber-900',
  Denied: 'bg-destructive/10 text-destructive',
}

export default function AuditLog() {
  const [params, setParams] = useSearchParams()
  const action = params.get('action') ?? ''
  const outcome = params.get('outcome') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [open, setOpen] = useState<number | null>(null)

  const actions = useQuery({ queryKey: ['audit', 'actions'], queryFn: () => get<string[]>('/api/audit-events/actions'), staleTime: Infinity })
  const events = useQuery({
    queryKey: ['audit', action, outcome, page],
    queryFn: () => get<Paged<AuditEvent>>(`/api/audit-events${query({ action, outcome, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = events.data ? Math.max(1, Math.ceil(events.data.total / pageSize)) : 1

  const setFilter = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value === all) next.delete(key)
    else next.set(key, value)
    next.delete('page')
    setParams(next)
  }
  const goTo = (p: number) => {
    const next = new URLSearchParams(params)
    next.set('page', String(p))
    setParams(next)
  }

  return (
    <>
      <PageHeader title="Audit log" description="Sign-ins, account changes and access to candidate records. Entries can’t be edited or deleted." />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap gap-3">
            <Select value={action || all} onValueChange={(v) => setFilter('action', v)}>
              <SelectTrigger className="w-64" aria-label="Filter by action">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={all}>All actions</SelectItem>
                {actions.data?.map((a) => (
                  <SelectItem key={a} value={a}>
                    {humanise(a)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={outcome || all} onValueChange={(v) => setFilter('outcome', v)}>
              <SelectTrigger className="w-44" aria-label="Filter by outcome">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={all}>All outcomes</SelectItem>
                <SelectItem value="Success">Success</SelectItem>
                <SelectItem value="Failure">Failure</SelectItem>
                <SelectItem value="Denied">Denied</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {events.isError ? (
            <FormError message="We couldn’t load the audit log. Please refresh the page." />
          ) : !events.data ? (
            <Skeleton className="h-96" />
          ) : events.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">No entries match these filters.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>When</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Outcome</TableHead>
                  <TableHead className="hidden md:table-cell">By</TableHead>
                  <TableHead className="hidden lg:table-cell">IP address</TableHead>
                  <TableHead className="w-10">
                    <span className="sr-only">Details</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {events.data.items.map((e) => (
                  <Fragment key={e.id}>
                    <TableRow>
                      <TableCell className="text-muted-foreground">{formatDateTime(e.occurredAtUtc)}</TableCell>
                      <TableCell className="font-medium text-ink">{humanise(e.action)}</TableCell>
                      <TableCell>
                        <Badge className={outcomeStyle[e.outcome]}>{e.outcome}</Badge>
                      </TableCell>
                      <TableCell className="hidden md:table-cell">{e.actorEmail ?? 'Anonymous'}</TableCell>
                      <TableCell className="hidden font-mono text-xs lg:table-cell">{e.ipAddress ?? '—'}</TableCell>
                      <TableCell>
                        <Button variant="ghost" size="icon-sm" aria-expanded={open === e.id} aria-label="Show details" onClick={() => setOpen(open === e.id ? null : e.id)}>
                          <ChevronDown className={cn('transition-transform', open === e.id && 'rotate-180')} />
                        </Button>
                      </TableCell>
                    </TableRow>
                    {open === e.id && (
                      <TableRow className="bg-muted/40 hover:bg-muted/40">
                        <TableCell colSpan={6} className="whitespace-normal">
                          <dl className="grid gap-2 text-xs sm:grid-cols-[8rem_1fr]">
                            <dt className="text-muted-foreground">Record</dt>
                            <dd className="font-mono break-all">{e.entityType ? `${e.entityType} ${e.entityId ?? ''}` : '—'}</dd>
                            <dt className="text-muted-foreground">Actor ID</dt>
                            <dd className="font-mono break-all">{e.actorUserId ?? '—'}</dd>
                            <dt className="text-muted-foreground">Details</dt>
                            <dd className="font-mono break-all">{e.details ?? '—'}</dd>
                          </dl>
                        </TableCell>
                      </TableRow>
                    )}
                  </Fragment>
                ))}
              </TableBody>
            </Table>
          )}

          {events.data && events.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {events.data.total} entries
              </span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => goTo(page - 1)}>
                  <ChevronLeft /> Newer
                </Button>
                <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => goTo(page + 1)}>
                  Older <ChevronRight />
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  )
}
