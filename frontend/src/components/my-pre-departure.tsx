import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { ApiError, get, put } from '@/lib/api'
import { dayText, todayIso } from '@/lib/admissions'
import type { PreDepartureResult } from '@/lib/visa'

const key = ['visa', 'pre-departure', 'mine'] as const
const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

/** "Before you travel": the student's pre-departure list, once staff have opened it after their visa is approved. */
export function MyPreDeparture() {
  const queryClient = useQueryClient()
  const lists = useQuery({ queryKey: key, queryFn: () => get<PreDepartureResult[]>('/api/visa-cases/mine/pre-departure'), staleTime: 0 })
  const [dateFor, setDateFor] = useState<PreDepartureResult | null>(null)
  const confirm = useMutation({
    mutationFn: (v: { id: string; confirmed: boolean }) => put<PreDepartureResult>(`/api/visa-cases/mine/pre-departure/items/${v.id}/confirm`, { confirmed: v.confirmed }),
    onSuccess: (r) => queryClient.setQueryData<PreDepartureResult[]>(key, (old) => old?.map((l) => (l.visaCaseId === r.visaCaseId ? r : l))),
    onError: (e) => toast.error(message(e)),
  })
  if (!lists.data?.length) return null

  return (
    <>
      {lists.data.map((l) => {
        const required = l.items.filter((i) => i.importance === 'Required')
        const done = required.filter((i) => i.completedAtUtc).length
        return (
          <Card key={l.visaCaseId}>
            <CardHeader>
              <CardTitle className="flex flex-wrap items-center gap-2 text-lg text-ink">
                Before you travel to {l.country} {l.readyToTravel && <Badge>Ready to travel</Badge>}
              </CardTitle>
              <CardDescription>
                Tick each item when you have it. Your counsellor then checks it. {done} of {required.length} required items checked.
              </CardDescription>
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              {l.caveat && <p className="rounded-lg bg-muted/60 p-3 text-xs text-muted-foreground">{l.caveat}</p>}
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-muted-foreground">
                  Planned departure: <span className="font-medium text-ink">{dayText(l.plannedDepartureOn)}</span>
                </p>
                <Button variant="ghost" size="sm" onClick={() => setDateFor(l)}>
                  {l.plannedDepartureOn ? 'Change' : 'Set the date'}
                </Button>
              </div>
              <ul className="grid gap-2">
                {l.items.map((i) => {
                  const id = `pd-${i.id}`
                  const locked = i.completedAtUtc !== null
                  return (
                    <li key={i.id} className="flex items-start gap-3 rounded-lg border p-3">
                      <Checkbox id={id} className="mt-0.5" checked={i.confirmedAtUtc !== null} disabled={locked || confirm.isPending} onCheckedChange={(v) => confirm.mutate({ id: i.id, confirmed: v === true })} />
                      <label htmlFor={id} className="grid min-w-0 flex-1 gap-0.5">
                        <span className="flex flex-wrap items-center gap-2 font-medium text-ink">
                          {i.title}
                          <Badge variant={i.importance === 'Required' ? 'secondary' : 'outline'}>{i.importance === 'Required' ? 'Required' : 'Advice'}</Badge>
                          {locked && <Badge>Checked</Badge>}
                        </span>
                        {i.detail && <span className="text-xs text-muted-foreground">{i.detail}</span>}
                        {i.note && <span className="text-xs text-ink">Note from your counsellor: {i.note}</span>}
                      </label>
                    </li>
                  )
                })}
              </ul>
            </CardContent>
          </Card>
        )
      })}
      {dateFor && <DepartureDialog list={dateFor} onClose={() => setDateFor(null)} onDone={() => void queryClient.invalidateQueries({ queryKey: key })} />}
    </>
  )
}

function DepartureDialog({ list, onClose, onDone }: { list: PreDepartureResult; onClose: () => void; onDone: () => void }) {
  const [day, setDay] = useState(list.plannedDepartureOn ?? '')
  const save = useMutation({
    mutationFn: () => put(`/api/visa-cases/${list.visaCaseId}/pre-departure/departure`, { plannedOn: day || null }),
    onSuccess: () => {
      onDone()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>When do you plan to leave?</DialogTitle>
          <DialogDescription>Tell us the day you plan to travel. You can change it later.</DialogDescription>
        </DialogHeader>
        <Field label="Departure date" type="date" min={todayIso()} value={day} onChange={(e) => setDay(e.target.value)} />
        <FormError message={save.error ? message(save.error) : null} />
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
