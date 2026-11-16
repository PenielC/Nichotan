import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, ChevronLeft, ChevronRight, Pencil, Plus, Search } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Checkbox } from '@/components/ui/checkbox'
import { ChecklistCard } from '@/pages/staff/Documents'
import { PreDepartureCard } from '@/pages/staff/PreDeparture'
import { TasksCard } from '@/pages/staff/Tasks'
import { Letter } from '@/pages/staff/Outcomes'
import type { DocumentTypeDto } from '@/lib/documents'
import { VisaStatusBadge } from '@/components/visa-status'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, get, post, put, query, type Paged } from '@/lib/api'
import { destinations } from '@/lib/application'
import { admissionsRoles, hasAnyRole, useAuth, visaRoles } from '@/lib/auth'
import { dayText, todayIso, type Outcome } from '@/lib/admissions'
import { formatDate, formatDateTime } from '@/lib/format'
import { allVisaStatuses, visaStatusLabel, type VisaDetail, type VisaStatus, type VisaSummary } from '@/lib/visa'

const pageSize = 25
const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

function useRoles() {
  const { state } = useAuth()
  const signedIn = state.status === 'signedIn'
  return { canManage: signedIn && hasAnyRole(state.session, visaRoles), canUploadLetters: signedIn && hasAnyRole(state.session, admissionsRoles), isAdmin: signedIn && state.session.user.roles.includes('Administrator') }
}

const ManualNote = () => <p className="text-xs text-muted-foreground">Statuses are entered by staff from what the immigration authority tells them. They aren’t synced with any government system.</p>

// ─────────────────────────── Visa cases list ───────────────────────────

export function VisaList() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const country = params.get('country') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [text, setText] = useState(search)

  const setFilters = (next: Record<string, string | number>) => {
    const merged: Record<string, string | number> = { search, status, country, page: 1, ...next }
    setParams(Object.fromEntries(Object.entries(merged).filter(([k, v]) => v && !(k === 'page' && v === 1)).map(([k, v]) => [k, String(v)])), { replace: true })
  }
  useEffect(() => {
    const timer = setTimeout(() => {
      if (text.trim() !== search) setFilters({ search: text.trim() })
    }, 300)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text])

  const list = useQuery({
    queryKey: ['visa', 'list', search, status, country, page],
    queryFn: () => get<Paged<VisaSummary>>(`/api/visa-cases${query({ search, status, country, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / pageSize)) : 1

  return (
    <>
      <PageHeader title="Visa cases" description="Each accepted student’s visa application and where it stands. Open a case from the admission application once the school’s acceptance is recorded." />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="relative min-w-60 flex-1 sm:max-w-md">
              <span className="sr-only">Search visa cases</span>
              <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <Input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search candidate or institution" className="h-10 pl-9" maxLength={100} />
            </label>
            <SelectField label="Status" className="w-60" placeholder="All statuses" options={allVisaStatuses.map((s) => ({ value: s, label: visaStatusLabel[s] }))} value={status} onChange={(e) => setFilters({ status: e.target.value })} />
            <SelectField label="Country" className="w-36" placeholder="All" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setFilters({ country: e.target.value })} />
          </div>
          <ManualNote />
          {list.isError ? (
            <FormError message="We couldn’t load visa cases. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-64" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search || status || country ? 'No visa cases match these filters.' : 'No visa cases yet.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Candidate</TableHead>
                  <TableHead>Institution</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="hidden md:table-cell">Biometrics</TableHead>
                  <TableHead className="hidden text-right md:table-cell">Last change</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((v) => (
                  <TableRow key={v.id} className="cursor-pointer" onClick={() => navigate(`/staff/visa/${v.id}`)}>
                    <TableCell className="font-medium text-ink">
                      <Link to={`/staff/visa/${v.id}`} onClick={(e) => e.stopPropagation()} className="hover:underline">
                        {v.candidateName}
                      </Link>
                    </TableCell>
                    <TableCell>
                      {v.institutionName}
                      <div className="text-xs text-muted-foreground">{v.country}</div>
                    </TableCell>
                    <TableCell>
                      <VisaStatusBadge status={v.status} />
                    </TableCell>
                    <TableCell className="hidden md:table-cell">{dayText(v.biometricsAppointmentOn)}</TableCell>
                    <TableCell className="hidden text-right text-muted-foreground md:table-cell">{formatDate(v.statusChangedAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          {list.data && list.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {list.data.total} cases
              </span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setFilters({ page: page - 1 })}>
                  <ChevronLeft /> Previous
                </Button>
                <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setFilters({ page: page + 1 })}>
                  Next <ChevronRight />
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  )
}

// ─────────────────────────── One visa case ───────────────────────────

export function VisaPage() {
  const { id = '' } = useParams()
  const { canManage, isAdmin } = useRoles()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: ['visa', id], queryFn: () => get<VisaDetail>(`/api/visa-cases/${id}`), retry: false })
  const [moveTo, setMoveTo] = useState<{ status: VisaStatus; noteRequired: boolean } | null>(null)
  const [correcting, setCorrecting] = useState(false)
  const [editing, setEditing] = useState(false)
  const update = (d: VisaDetail) => {
    queryClient.setQueryData(['visa', id], d)
    void queryClient.invalidateQueries({ queryKey: ['visa', 'list'] })
    void queryClient.invalidateQueries({ queryKey: ['visa', 'stats'] })
    void queryClient.invalidateQueries({ queryKey: ['visa', 'application'] })
  }

  if (detail.isError) return <FormError message={detail.error instanceof ApiError && detail.error.status === 404 ? 'This visa case doesn’t exist.' : 'We couldn’t load this visa case.'} />
  if (!detail.data) return <Skeleton className="h-96" />
  const d = detail.data
  const v = d.summary
  const finished = d.allowedMoves.length === 0

  return (
    <div className="grid gap-6">
      <div>
        <Button asChild variant="ghost" size="sm" className="mb-2 -ml-2">
          <Link to={`/staff/candidates/${v.candidateId}`}>
            <ArrowLeft /> {v.candidateName}
          </Link>
        </Button>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-bold text-ink">{v.country} visa</h1>
          <VisaStatusBadge status={v.status} />
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          For{' '}
          <Link to={`/staff/admissions/${v.applicationId}`} className="text-primary underline-offset-2 hover:underline">
            {v.programmeName} at {v.institutionName}
          </Link>
        </p>
      </div>
      <ManualNote />

      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg text-ink">History</CardTitle>
            <CardDescription>Every move, with the day it happened and who recorded it. Nothing is overwritten.</CardDescription>
          </CardHeader>
          <CardContent>
            <ol className="grid gap-4">
              {[...d.history].reverse().map((e) => (
                <li key={e.id} className="border-l-2 border-primary/30 pl-4">
                  <p className="flex flex-wrap items-center gap-2 text-sm font-medium text-ink">
                    {e.from === null ? 'Visa case opened' : visaStatusLabel[e.to]}
                    {e.isCorrection && <Badge variant="destructive">Correction</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {dayText(e.occurredOn)} · recorded {formatDateTime(e.recordedAtUtc)}
                    {e.recordedBy ? ` by ${e.recordedBy}` : ''}
                    {e.from ? ` · from ${visaStatusLabel[e.from]}` : ''}
                  </p>
                  {e.note && e.from !== null && <p className="mt-1 text-sm whitespace-pre-wrap text-ink">{e.note}</p>}
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>

        <div className="grid content-start gap-6">
          <PreDepartureCard visaCaseId={v.id} candidateId={v.candidateId} approved={v.status === 'Approved'} />
          <TasksCard candidateId={v.candidateId} visaCaseId={v.id} />
          <ChecklistCard applicationId={v.applicationId} candidateId={v.candidateId} visaCaseId={v.id} finished={finished} title="Visa documents" />
          <LettersCard detail={d} onChanged={() => void queryClient.invalidateQueries({ queryKey: ['visa', id] })} />
          {canManage && (
            <Card>
              <CardHeader>
                <CardTitle className="text-lg text-ink">Record a move</CardTitle>
                <CardDescription>{finished ? 'This visa case is finished.' : 'What did the authority tell us, or what did we do?'}</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-2">
                {d.allowedMoves.map((m) => (
                  <Button key={m.status} variant="outline" className="justify-start" onClick={() => setMoveTo(m)}>
                    {visaStatusLabel[m.status]}
                  </Button>
                ))}
                {isAdmin && (
                  <Button variant="ghost" size="sm" className="mt-2 justify-start text-muted-foreground" onClick={() => setCorrecting(true)}>
                    <AlertTriangle /> Correct a wrong status
                  </Button>
                )}
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader className="flex-row items-start justify-between gap-2">
              <div>
                <CardTitle className="text-lg text-ink">Case details</CardTitle>
                <CardDescription>From the authority’s system.</CardDescription>
              </div>
              {canManage && !finished && (
                <Button variant="ghost" size="icon" aria-label="Edit case details" onClick={() => setEditing(true)}>
                  <Pencil />
                </Button>
              )}
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              <Detail label="Their reference" value={v.referenceNumber ?? '—'} />
              <Detail label="Submitted on" value={dayText(v.submittedOn)} />
              <Detail label="Biometrics appointment" value={v.biometricsAppointmentOn ? `${dayText(v.biometricsAppointmentOn)}${d.biometricsLocation ? ` · ${d.biometricsLocation}` : ''}` : '—'} />
              <Detail label="Decision on" value={dayText(v.decisionOn)} />
              <p className="text-xs text-muted-foreground">Never save the candidate’s immigration-portal password here.</p>
            </CardContent>
          </Card>
        </div>
      </div>

      {moveTo && <MoveDialog key={moveTo.status} detail={d} move={moveTo} onClose={() => setMoveTo(null)} onDone={update} />}
      {correcting && <CorrectDialog detail={d} onClose={() => setCorrecting(false)} onDone={update} />}
      {editing && <DetailsDialog detail={d} onClose={() => setEditing(false)} onDone={update} />}
    </div>
  )
}

/** The authority's own letters: the biometrics request (usually sent to the student) and the decision. */
function LettersCard({ detail: d, onChanged }: { detail: VisaDetail; onChanged: () => void }) {
  const { canUploadLetters } = useRoles()
  const types = useQuery({ queryKey: ['document-types'], queryFn: () => get<DocumentTypeDto[]>('/api/document-types') })
  const typeId = (purpose: 'BiometricsLetter' | 'DecisionLetter') => types.data?.find((t) => t.purpose === purpose)?.id
  const v = d.summary
  const showBiometrics = d.biometricsLetter !== null || ['Submitted', 'BiometricsRequested', 'BiometricsCompleted', 'AdditionalInformationRequested'].includes(v.status)
  const showDecision = d.decisionLetter !== null || v.status === 'Approved' || v.status === 'Refused'
  if (!showBiometrics && !showDecision) return null
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg text-ink">Letters from the authority</CardTitle>
        <CardDescription>Kept as received. The student can add the biometrics letter themselves.</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3">
        {showBiometrics && <Letter label="Biometrics letter" doc={d.biometricsLetter} typeId={typeId('BiometricsLetter')} applicationId={v.applicationId} candidateId={v.candidateId} canUpload={canUploadLetters} onUploaded={onChanged} />}
        {showDecision && <Letter label="Decision letter" doc={d.decisionLetter} typeId={typeId('DecisionLetter')} applicationId={v.applicationId} candidateId={v.candidateId} canUpload={canUploadLetters} onUploaded={onChanged} />}
      </CardContent>
    </Card>
  )
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</p>
      <p className="text-ink">{value}</p>
    </div>
  )
}

const noteBox = 'w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50'

function MoveDialog({ detail, move, onClose, onDone }: { detail: VisaDetail; move: { status: VisaStatus; noteRequired: boolean }; onClose: () => void; onDone: (d: VisaDetail) => void }) {
  const submitting = move.status === 'Submitted'
  const biometrics = move.status === 'BiometricsRequested'
  const [date, setDate] = useState(todayIso)
  const [note, setNote] = useState('')
  const [reference, setReference] = useState(detail.summary.referenceNumber ?? '')
  const [appointment, setAppointment] = useState(detail.summary.biometricsAppointmentOn ?? '')
  const [location, setLocation] = useState(detail.biometricsLocation ?? '')
  const [proceed, setProceed] = useState(false)
  const outstanding = submitting && detail.summary.status === 'Preparing' ? detail.documentGaps.missing : 0
  const save = useMutation({
    mutationFn: () =>
      post<VisaDetail>(`/api/visa-cases/${detail.summary.id}/status`, {
        acknowledgeDocumentGaps: proceed,
        toStatus: move.status,
        occurredOn: date || null,
        note: note.trim() || null,
        referenceNumber: submitting ? reference.trim() || null : null,
        biometricsAppointmentOn: biometrics ? appointment || null : null,
        biometricsLocation: biometrics ? location.trim() || null : null,
      }),
    onSuccess: (d) => {
      toast.success(`Recorded: ${visaStatusLabel[move.status]}.`)
      onDone(d)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record: {visaStatusLabel[move.status]}</DialogTitle>
          <DialogDescription>From {visaStatusLabel[detail.summary.status]}. This is added to the history.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          {outstanding > 0 && (
            <Alert variant="destructive">
              <AlertTriangle />
              <AlertTitle>
                {outstanding} required visa document{outstanding === 1 ? ' is' : 's are'} not yet uploaded
              </AlertTitle>
              <AlertDescription>
                <div className="mt-2 flex items-start gap-2">
                  <Checkbox id="visa-proceed" checked={proceed} onCheckedChange={(c) => setProceed(c === true)} className="mt-0.5" />
                  <Label htmlFor="visa-proceed" className="leading-snug font-normal">
                    Continue anyway. This is noted in the history.
                  </Label>
                </div>
              </AlertDescription>
            </Alert>
          )}
          <Field label="Date it happened" type="date" max={todayIso()} value={date} onChange={(e) => setDate(e.target.value)} hint="Use the day it happened, even if you’re recording it later." />
          {submitting && <Field label="Authority’s reference (optional)" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />}
          {biometrics && (
            <>
              <Field label="Appointment date (optional)" type="date" value={appointment} onChange={(e) => setAppointment(e.target.value)} />
              <Field label="Where (optional)" value={location} onChange={(e) => setLocation(e.target.value)} maxLength={300} />
            </>
          )}
          <div className="grid gap-1.5">
            <Label htmlFor="visa-note">Note{move.noteRequired ? <span className="text-destructive"> *</span> : ' (optional)'}</Label>
            <textarea id="visa-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className={noteBox} />
          </div>
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={save.isPending || (move.noteRequired && !note.trim()) || !date || (outstanding > 0 && !proceed)}>
            Record
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function CorrectDialog({ detail, onClose, onDone }: { detail: VisaDetail; onClose: () => void; onDone: (d: VisaDetail) => void }) {
  const [to, setTo] = useState('')
  const [date, setDate] = useState(todayIso)
  const [note, setNote] = useState('')
  const save = useMutation({
    mutationFn: () => post<VisaDetail>(`/api/visa-cases/${detail.summary.id}/correct`, { toStatus: to, occurredOn: date || null, note: note.trim() }),
    onSuccess: (d) => {
      toast.success('The status has been corrected.')
      onDone(d)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Correct the status</DialogTitle>
          <DialogDescription>Use this only for a status that was recorded wrongly. The earlier entry stays in the history, with your correction after it.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <SelectField label="Correct status" required options={allVisaStatuses.filter((s) => s !== detail.summary.status).map((s) => ({ value: s, label: visaStatusLabel[s] }))} value={to} onChange={(e) => setTo(e.target.value)} />
          <Field label="Date it applies from" type="date" max={todayIso()} value={date} onChange={(e) => setDate(e.target.value)} />
          <div className="grid gap-1.5">
            <Label htmlFor="visa-correct-note">What is being corrected? <span className="text-destructive">*</span></Label>
            <textarea id="visa-correct-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className={noteBox} />
          </div>
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!to || !note.trim() || save.isPending}>
            Correct
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function DetailsDialog({ detail, onClose, onDone }: { detail: VisaDetail; onClose: () => void; onDone: (d: VisaDetail) => void }) {
  const [reference, setReference] = useState(detail.summary.referenceNumber ?? '')
  const [appointment, setAppointment] = useState(detail.summary.biometricsAppointmentOn ?? '')
  const [location, setLocation] = useState(detail.biometricsLocation ?? '')
  const save = useMutation({
    mutationFn: () => put<VisaDetail>(`/api/visa-cases/${detail.summary.id}/details`, { referenceNumber: reference.trim() || null, biometricsAppointmentOn: appointment || null, biometricsLocation: location.trim() || null }),
    onSuccess: (d) => {
      toast.success('Case details saved.')
      onDone(d)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Case details</DialogTitle>
          <DialogDescription>The authority’s reference and the biometrics appointment. Never enter a password.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="Authority’s reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
          <Field label="Biometrics appointment date" type="date" value={appointment} onChange={(e) => setAppointment(e.target.value)} />
          <Field label="Biometrics location" value={location} onChange={(e) => setLocation(e.target.value)} maxLength={300} />
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

// ─────────────────────────── On the admission application page ───────────────────────────

/** Shows the visa case for an application, or lets visa staff open one once the school's acceptance is recorded. */
export function VisaCaseCard({ applicationId }: { applicationId: string }) {
  const { canManage } = useRoles()
  const queryClient = useQueryClient()
  const existing = useQuery({ queryKey: ['visa', 'application', applicationId], queryFn: () => get<Paged<VisaSummary>>(`/api/visa-cases${query({ applicationId, pageSize: 1 })}`) })
  const open = useMutation({
    mutationFn: () => post<VisaDetail>('/api/visa-cases', { applicationId }),
    onSuccess: () => {
      toast.success('Visa case opened.')
      void queryClient.invalidateQueries({ queryKey: ['visa'] })
    },
    onError: (e) => toast.error(message(e)),
  })
  const outcome = useQuery({ queryKey: ['admissions', 'outcome', applicationId], queryFn: () => get<Outcome>(`/api/admissions/${applicationId}/outcome`) })
  const visa = existing.data?.items[0]
  if (!visa && !outcome.data?.acceptance) return null
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg text-ink">Visa</CardTitle>
        <CardDescription>{visa ? 'This student’s visa application.' : 'The school’s acceptance is recorded, so the visa application can start.'}</CardDescription>
      </CardHeader>
      <CardContent>
        {existing.isLoading ? (
          <Skeleton className="h-10" />
        ) : visa ? (
          <Link to={`/staff/visa/${visa.id}`} className="flex flex-wrap items-center justify-between gap-2 text-sm hover:underline">
            <VisaStatusBadge status={visa.status} />
            <span className="text-primary">Open visa case</span>
          </Link>
        ) : canManage ? (
          <Button onClick={() => open.mutate()} disabled={open.isPending}>
            <Plus /> Open visa case
          </Button>
        ) : (
          <p className="text-sm text-muted-foreground">No visa case yet.</p>
        )}
      </CardContent>
    </Card>
  )
}
