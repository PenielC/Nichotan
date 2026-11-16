import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Pencil, Plus, Undo2 } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError, SelectField } from '@/components/form'
import { WhatsAppShareButton } from '@/components/whatsapp-share'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, get, post, put } from '@/lib/api'
import { dayText, todayIso } from '@/lib/admissions'
import { destinations, type DestinationCountry } from '@/lib/application'
import { hasAnyRole, useAuth, visaRoles } from '@/lib/auth'
import { formatDate } from '@/lib/format'
import type { PreDepartureImportance, PreDepartureItem, PreDepartureResult, PreDepartureTemplate, PreDepartureTemplateItem } from '@/lib/visa'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const importanceOptions = [
  { value: 'Required', label: 'Required' },
  { value: 'Recommended', label: 'Recommended (advice)' },
]
const textArea = 'w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50'

// ─────────────────────────── On a visa case ───────────────────────────

/** The pre-departure list for an approved visa: staff activate it, the student confirms items, staff verify them. */
export function PreDepartureCard({ visaCaseId, candidateId, approved }: { visaCaseId: string; candidateId: string; approved: boolean }) {
  const { state } = useAuth()
  const canManage = state.status === 'signedIn' && hasAnyRole(state.session, visaRoles)
  const queryClient = useQueryClient()
  const key = ['visa', 'pre-departure', visaCaseId]
  const list = useQuery({ queryKey: key, queryFn: () => get<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure`) })
  const [editing, setEditing] = useState<PreDepartureItem | null>(null)
  const [adding, setAdding] = useState(false)
  const [dateOpen, setDateOpen] = useState(false)
  const update = (r: PreDepartureResult) => queryClient.setQueryData(key, r)
  const activate = useMutation({
    mutationFn: () => post<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure`),
    onSuccess: (r) => {
      update(r)
      toast.success('The pre-departure list is open. The student has been emailed.')
    },
    onError: (e) => toast.error(message(e)),
  })
  const verify = useMutation({
    mutationFn: (v: { id: string; verified: boolean }) => post<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure/items/${v.id}/verify`, { verified: v.verified }),
    onSuccess: update,
    onError: (e) => toast.error(message(e)),
  })

  const r = list.data
  if (!approved && !r?.activated) return null
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2 text-lg text-ink">
          Pre-departure list {r?.readyToTravel && <Badge>Ready to travel</Badge>}
        </CardTitle>
        <CardDescription>
          {r?.activated ? 'The student confirms each item; you check it. Required items must all be checked before they are ready to travel.' : 'The visa is approved. Open the student’s list of what to prepare and bring.'}
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3 text-sm">
        {list.isLoading || !r ? (
          <Skeleton className="h-16" />
        ) : !r.activated ? (
          canManage ? (
            <Button onClick={() => activate.mutate()} disabled={activate.isPending} className="justify-self-start">
              <Plus /> Open the {r.country} list
            </Button>
          ) : (
            <p className="text-muted-foreground">Not opened yet.</p>
          )
        ) : (
          <>
            {r.caveat && <p className="rounded-lg bg-muted/60 p-3 text-xs text-muted-foreground">{r.caveat}</p>}
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-muted-foreground">Planned departure: <span className="font-medium text-ink">{dayText(r.plannedDepartureOn)}</span></p>
              {canManage && (
                <Button variant="ghost" size="sm" onClick={() => setDateOpen(true)}>
                  <Pencil /> Change
                </Button>
              )}
            </div>
            <ul className="grid gap-2">
              {r.items.map((i) => (
                <li key={i.id} className={`grid gap-1 rounded-lg border p-3 ${i.isActive ? '' : 'opacity-60'}`}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
                      {i.title}
                      <Badge variant={i.importance === 'Required' ? 'secondary' : 'outline'}>{i.importance === 'Required' ? 'Required' : 'Advice'}</Badge>
                      {i.source === 'Manual' && <Badge variant="outline">Added by staff</Badge>}
                      {!i.isActive && <Badge variant="outline">Not needed</Badge>}
                    </p>
                    {canManage && i.isActive && (
                      <div className="flex items-center gap-1">
                        {i.completedAtUtc ? (
                          <Button variant="ghost" size="sm" onClick={() => verify.mutate({ id: i.id, verified: false })} disabled={verify.isPending}>
                            <Undo2 /> Undo
                          </Button>
                        ) : (
                          <Button variant="outline" size="sm" onClick={() => verify.mutate({ id: i.id, verified: true })} disabled={verify.isPending}>
                            <Check /> Checked
                          </Button>
                        )}
                        <Button variant="ghost" size="icon" aria-label={`Edit ${i.title}`} onClick={() => setEditing(i)}>
                          <Pencil />
                        </Button>
                      </div>
                    )}
                    {canManage && !i.isActive && (
                      <Button variant="ghost" size="sm" onClick={() => setEditing(i)}>
                        Edit
                      </Button>
                    )}
                  </div>
                  {i.detail && <p className="text-xs text-muted-foreground">{i.detail}</p>}
                  {i.note && <p className="text-xs text-ink">Note: {i.note}</p>}
                  <p className="text-xs text-muted-foreground">
                    {i.completedAtUtc ? `Checked ${formatDate(i.completedAtUtc)}${i.verifiedBy ? ` by ${i.verifiedBy}` : ''}` : i.confirmedAtUtc ? `Student says they have it (${formatDate(i.confirmedAtUtc)}). Not checked yet.` : 'Not confirmed by the student yet.'}
                  </p>
                </li>
              ))}
            </ul>
            {canManage && (
              <div className="flex flex-wrap gap-2">
                <Button variant="outline" size="sm" onClick={() => setAdding(true)}>
                  <Plus /> Add an item
                </Button>
                <WhatsAppShareButton
                  candidateId={candidateId}
                  topic="pre-departure"
                  intro={`Your pre-departure list for ${r.country} is ready. Here's what to prepare and bring:`}
                  lines={r.items.filter((i) => i.isActive).map((i) => (i.importance === 'Required' ? i.title : `${i.title} (advice)`))}
                  portalPath="/portal/applications"
                  label="Send the list on WhatsApp"
                />
              </div>
            )}
          </>
        )}
      </CardContent>
      {editing && <ItemDialog visaCaseId={visaCaseId} item={editing} onClose={() => setEditing(null)} onDone={update} />}
      {adding && <AddDialog visaCaseId={visaCaseId} onClose={() => setAdding(false)} onDone={update} />}
      {dateOpen && r && <DateDialog visaCaseId={visaCaseId} current={r.plannedDepartureOn} onClose={() => setDateOpen(false)} onDone={update} />}
    </Card>
  )
}

function ItemDialog({ visaCaseId, item, onClose, onDone }: { visaCaseId: string; item: PreDepartureItem; onClose: () => void; onDone: (r: PreDepartureResult) => void }) {
  const [note, setNote] = useState(item.note ?? '')
  const [importance, setImportance] = useState<PreDepartureImportance>(item.importance)
  const [needed, setNeeded] = useState(item.isActive)
  const save = useMutation({
    mutationFn: () => put<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure/items/${item.id}`, { note: note.trim() || null, importance, isActive: needed }),
    onSuccess: (r) => {
      onDone(r)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{item.title}</DialogTitle>
          <DialogDescription>Add a note the student will see, change how important it is, or switch it off if it doesn’t apply to them.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <SelectField label="Importance" options={importanceOptions} value={importance} onChange={(e) => setImportance(e.target.value as PreDepartureImportance)} />
          <div className="grid gap-1.5">
            <Label htmlFor="pd-note">Note</Label>
            <textarea id="pd-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={500} className={textArea} />
          </div>
          <div className="flex items-center gap-2">
            <Checkbox id="pd-needed" checked={needed} onCheckedChange={(v) => setNeeded(v === true)} />
            <Label htmlFor="pd-needed" className="font-normal">
              This applies to the student
            </Label>
          </div>
          <FormError message={save.error ? message(save.error) : null} />
        </div>
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

function AddDialog({ visaCaseId, onClose, onDone }: { visaCaseId: string; onClose: () => void; onDone: (r: PreDepartureResult) => void }) {
  const [title, setTitle] = useState('')
  const [detail, setDetail] = useState('')
  const [importance, setImportance] = useState<PreDepartureImportance>('Required')
  const save = useMutation({
    mutationFn: () => post<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure/items`, { title: title.trim(), detail: detail.trim() || null, importance }),
    onSuccess: (r) => {
      onDone(r)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add an item</DialogTitle>
          <DialogDescription>For this student only. To change the list for everyone, an administrator edits the template under Checklists.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="What to prepare or bring" required value={title} onChange={(e) => setTitle(e.target.value)} maxLength={150} />
          <Field label="More detail (optional)" value={detail} onChange={(e) => setDetail(e.target.value)} maxLength={500} />
          <SelectField label="Importance" options={importanceOptions} value={importance} onChange={(e) => setImportance(e.target.value as PreDepartureImportance)} />
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!title.trim() || save.isPending}>
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DateDialog({ visaCaseId, current, onClose, onDone }: { visaCaseId: string; current: string | null; onClose: () => void; onDone: (r: PreDepartureResult) => void }) {
  const [day, setDay] = useState(current ?? '')
  const save = useMutation({
    mutationFn: () => put<PreDepartureResult>(`/api/visa-cases/${visaCaseId}/pre-departure/departure`, { plannedOn: day || null }),
    onSuccess: (r) => {
      onDone(r)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Planned departure</DialogTitle>
          <DialogDescription>The day the student plans to leave, if they’ve said.</DialogDescription>
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

// ─────────────────────────── The country templates (administrators) ───────────────────────────

/** Administrators keep each country's list and the wording shown with it. Changes apply to lists opened afterwards. */
export function PreDepartureTemplateCard() {
  const queryClient = useQueryClient()
  const template = useQuery({ queryKey: ['pre-departure-template'], queryFn: () => get<PreDepartureTemplate>('/api/pre-departure-template') })
  const [country, setCountry] = useState<DestinationCountry>('Canada')
  const [editing, setEditing] = useState<PreDepartureTemplateItem | 'new' | null>(null)
  const [wording, setWording] = useState<string | null>(null)
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['pre-departure-template'] })
  const caveat = template.data?.caveats.find((c) => c.country === country)?.text ?? ''
  const saveCaveat = useMutation({
    mutationFn: (text: string) => put(`/api/pre-departure-template/caveats/${country}`, { text }),
    onSuccess: () => {
      toast.success('Wording saved.')
      setWording(null)
      refresh()
    },
    onError: (e) => toast.error(message(e)),
  })
  const items = (template.data?.items ?? []).filter((i) => i.country === country)

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
        <div>
          <CardTitle className="text-lg text-ink">Pre-departure lists</CardTitle>
          <CardDescription>What each country’s student is told to prepare and bring after their visa is approved. A list is copied when staff open it; later edits don’t change lists already open.</CardDescription>
        </div>
        <Button variant="outline" size="sm" onClick={() => setEditing('new')}>
          <Plus /> Add an item
        </Button>
      </CardHeader>
      <CardContent className="grid gap-4">
        <SelectField label="Country" className="w-40" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value as DestinationCountry)} />
        {!template.data ? (
          <Skeleton className="h-32" />
        ) : (
          <>
            <ul className="grid gap-2">
              {items.map((i) => (
                <li key={i.id} className={`flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm ${i.isActive ? '' : 'opacity-60'}`}>
                  <span className="min-w-0">
                    <span className="flex flex-wrap items-center gap-2 font-medium text-ink">
                      {i.title}
                      <Badge variant={i.importance === 'Required' ? 'secondary' : 'outline'}>{i.importance === 'Required' ? 'Required' : 'Advice'}</Badge>
                      {!i.isActive && <Badge variant="outline">Off</Badge>}
                    </span>
                    {i.detail && <span className="block text-xs text-muted-foreground">{i.detail}</span>}
                  </span>
                  <Button variant="ghost" size="icon" aria-label={`Edit ${i.title}`} onClick={() => setEditing(i)}>
                    <Pencil />
                  </Button>
                </li>
              ))}
              {items.length === 0 && <li className="py-4 text-center text-sm text-muted-foreground">No items for {country} yet.</li>}
            </ul>
            <div className="grid gap-1.5">
              <Label htmlFor="pd-caveat">Wording shown with the list</Label>
              <textarea id="pd-caveat" rows={3} maxLength={1500} className={textArea} value={wording ?? caveat} onChange={(e) => setWording(e.target.value)} />
              <div className="flex justify-end">
                <Button size="sm" variant="outline" disabled={wording === null || !wording.trim() || saveCaveat.isPending} onClick={() => saveCaveat.mutate(wording!.trim())}>
                  Save wording
                </Button>
              </div>
              <p className="text-xs text-muted-foreground">Nichotan should approve this wording and each list against {country}’s current rules before students rely on them.</p>
            </div>
          </>
        )}
      </CardContent>
      {editing && <TemplateItemDialog target={editing} country={country} onClose={() => setEditing(null)} onSaved={refresh} />}
    </Card>
  )
}

function TemplateItemDialog({ target, country, onClose, onSaved }: { target: PreDepartureTemplateItem | 'new'; country: DestinationCountry; onClose: () => void; onSaved: () => void }) {
  const existing = target === 'new' ? null : target
  const [title, setTitle] = useState(existing?.title ?? '')
  const [detail, setDetail] = useState(existing?.detail ?? '')
  const [importance, setImportance] = useState<PreDepartureImportance>(existing?.importance ?? 'Required')
  const [sortOrder, setSortOrder] = useState(String(existing?.sortOrder ?? 500))
  const [active, setActive] = useState(existing?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { country: existing?.country ?? country, title: title.trim(), detail: detail.trim() || null, importance, sortOrder: Number(sortOrder) || 0, isActive: active }
      return existing ? put(`/api/pre-departure-template/items/${existing.id}`, body) : post('/api/pre-departure-template/items', body)
    },
    onSuccess: () => {
      toast.success('Saved. Lists opened from now on will use it.')
      onSaved()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{existing ? 'Edit item' : `Add an item to the ${country} list`}</DialogTitle>
          <DialogDescription>Switch an item off to stop using it without losing it.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="Item" required value={title} onChange={(e) => setTitle(e.target.value)} maxLength={150} />
          <Field label="More detail (optional)" value={detail} onChange={(e) => setDetail(e.target.value)} maxLength={500} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Importance" options={importanceOptions} value={importance} onChange={(e) => setImportance(e.target.value as PreDepartureImportance)} />
            <Field label="Order" type="number" min={0} max={10000} value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} hint="Lower comes first." />
          </div>
          {existing && (
            <div className="flex items-center gap-2">
              <Checkbox id="pdt-active" checked={active} onCheckedChange={(v) => setActive(v === true)} />
              <Label htmlFor="pdt-active" className="font-normal">
                Item is on
              </Label>
            </div>
          )}
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!title.trim() || save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
