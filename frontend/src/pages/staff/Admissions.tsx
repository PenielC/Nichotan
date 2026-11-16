import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, ChevronLeft, ChevronRight, ExternalLink, Pencil, Plus, Search } from 'lucide-react'
import { toast } from 'sonner'
import { AdmissionStatusBadge } from '@/components/admission-status'
import { Field, FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, get, post, put, query, type Paged } from '@/lib/api'
import { ChecklistCard } from '@/pages/staff/Documents'
import { OutcomeCard } from '@/pages/staff/Outcomes'
import { TasksCard } from '@/pages/staff/Tasks'
import { VisaCaseCard } from '@/pages/staff/Visas'
import { Checkbox } from '@/components/ui/checkbox'
import { applicantLevelLabel, destinations } from '@/lib/application'
import { admissionsRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { allStatuses, dayText, intakeText, statusLabel, todayIso, type AdmissionDetail, type AdmissionStatus, type AdmissionSummary, type Institution, type Programme } from '@/lib/admissions'
import { formatDateTime, formatDate } from '@/lib/format'

const pageSize = 25
const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

function useRoles() {
  const { state } = useAuth()
  const signedIn = state.status === 'signedIn'
  return {
    canManage: signedIn && hasAnyRole(state.session, admissionsRoles),
    isAdmin: signedIn && state.session.user.roles.includes('Administrator'),
  }
}

const ManualNote = () => (
  <p className="text-xs text-muted-foreground">Statuses are entered by staff from the school’s own portal or emails. They aren’t synced with any institution.</p>
)

// ─────────────────────────── Pipeline list ───────────────────────────

export function AdmissionList() {
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
    queryKey: ['admissions', 'list', search, status, country, page],
    queryFn: () => get<Paged<AdmissionSummary>>(`/api/admissions${query({ search, status, country, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / pageSize)) : 1

  return (
    <>
      <PageHeader title="Admission applications" description="Every candidate’s applications to institutions, and where each one stands." />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="relative min-w-60 flex-1 sm:max-w-md">
              <span className="sr-only">Search applications</span>
              <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <Input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search candidate, institution or programme" className="h-10 pl-9" maxLength={100} />
            </label>
            <SelectField label="Status" className="w-56" placeholder="All statuses" options={allStatuses.map((s) => ({ value: s, label: statusLabel[s] }))} value={status} onChange={(e) => setFilters({ status: e.target.value })} />
            <SelectField label="Country" className="w-36" placeholder="All" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setFilters({ country: e.target.value })} />
          </div>
          <ManualNote />

          {list.isError ? (
            <FormError message="We couldn’t load applications. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-64" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search || status || country ? 'No applications match these filters.' : 'No applications yet. Add them from a candidate’s record.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Candidate</TableHead>
                  <TableHead>Institution and programme</TableHead>
                  <TableHead className="hidden md:table-cell">Intake</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="hidden text-right md:table-cell">Last change</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((a) => (
                  <TableRow key={a.id} className="cursor-pointer" onClick={() => navigate(`/staff/admissions/${a.id}`)}>
                    <TableCell className="font-medium text-ink">
                      <Link to={`/staff/admissions/${a.id}`} onClick={(e) => e.stopPropagation()} className="hover:underline">
                        {a.candidateName}
                      </Link>
                    </TableCell>
                    <TableCell>
                      {a.institutionName}
                      <div className="text-xs text-muted-foreground">
                        {a.programmeName} · {a.country}
                      </div>
                    </TableCell>
                    <TableCell className="hidden md:table-cell">{intakeText(a.intakeTerm, a.intakeYear)}</TableCell>
                    <TableCell>
                      <AdmissionStatusBadge status={a.status} />
                    </TableCell>
                    <TableCell className="hidden text-right text-muted-foreground md:table-cell">{formatDate(a.statusChangedAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {list.data && list.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {list.data.total} applications
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

// ─────────────────────────── One application ───────────────────────────

export function AdmissionPage() {
  const { id = '' } = useParams()
  const { canManage, isAdmin } = useRoles()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: ['admissions', id], queryFn: () => get<AdmissionDetail>(`/api/admissions/${id}`), retry: false })
  const [moveTo, setMoveTo] = useState<{ status: AdmissionStatus; noteRequired: boolean } | null>(null)
  const [correcting, setCorrecting] = useState(false)
  const [editing, setEditing] = useState(false)
  const update = (d: AdmissionDetail) => {
    queryClient.setQueryData(['admissions', id], d)
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'list'] })
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'candidate'] })
  }

  if (detail.isError) return <FormError message={detail.error instanceof ApiError && detail.error.status === 404 ? 'This application doesn’t exist.' : 'We couldn’t load this application.'} />
  if (!detail.data) return <Skeleton className="h-96" />
  const d = detail.data
  const a = d.summary
  const finished = d.allowedMoves.length === 0

  return (
    <div className="grid gap-6">
      <div>
        <Button asChild variant="ghost" size="sm" className="mb-2 -ml-2">
          <Link to={`/staff/candidates/${a.candidateId}`}>
            <ArrowLeft /> {a.candidateName}
          </Link>
        </Button>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-bold text-ink">{a.institutionName}</h1>
          <AdmissionStatusBadge status={a.status} />
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          {a.programmeName} · {applicantLevelLabel[a.level]} · {a.country} · {intakeText(a.intakeTerm, a.intakeYear)}
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
                    {statusLabel[e.to]}
                    {e.isCorrection && <Badge variant="destructive">Correction</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {dayText(e.occurredOn)} · recorded {formatDateTime(e.recordedAtUtc)}
                    {e.recordedBy ? ` by ${e.recordedBy}` : ''}
                    {e.from ? ` · from ${statusLabel[e.from]}` : ''}
                  </p>
                  {e.note && <p className="mt-1 text-sm whitespace-pre-wrap text-ink">{e.note}</p>}
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>

        <div className="grid content-start gap-6">
          <OutcomeCard applicationId={a.id} candidateId={a.candidateId} status={a.status} />
          <VisaCaseCard applicationId={a.id} />
          <TasksCard candidateId={a.candidateId} applicationId={a.id} />
          <ChecklistCard applicationId={a.id} candidateId={a.candidateId} finished={finished} />
          {canManage && (
            <Card>
              <CardHeader>
                <CardTitle className="text-lg text-ink">Record a move</CardTitle>
                <CardDescription>{finished ? 'This application is finished.' : 'What happened on the school’s side, or in our office?'}</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-2">
                {d.allowedMoves.map((m) => (
                  <Button key={m.status} variant="outline" className="justify-start" onClick={() => setMoveTo(m)}>
                    {statusLabel[m.status]}
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
                <CardTitle className="text-lg text-ink">School details</CardTitle>
                <CardDescription>From the school’s portal.</CardDescription>
              </div>
              {canManage && !finished && (
                <Button variant="ghost" size="icon" aria-label="Edit school details" onClick={() => setEditing(true)}>
                  <Pencil />
                </Button>
              )}
            </CardHeader>
            <CardContent className="grid gap-3 text-sm">
              <div>
                <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Their reference</p>
                <p className="text-ink">{a.institutionReference ?? '—'}</p>
              </div>
              <div>
                <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Submitted on their portal</p>
                <p className="text-ink">{dayText(a.submittedToInstitutionOn)}</p>
              </div>
              <div>
                <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Portal link</p>
                {d.portalUrl ? (
                  <a href={d.portalUrl} target="_blank" rel="noreferrer noopener" className="inline-flex items-center gap-1 text-primary underline-offset-2 hover:underline">
                    Open on the school’s portal <ExternalLink className="size-3.5" />
                  </a>
                ) : (
                  <p className="text-ink">—</p>
                )}
              </div>
              <p className="text-xs text-muted-foreground">Never save the candidate’s portal password here.</p>
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

function MoveDialog({ detail, move, onClose, onDone }: { detail: AdmissionDetail; move: { status: AdmissionStatus; noteRequired: boolean }; onClose: () => void; onDone: (d: AdmissionDetail) => void }) {
  const submitting = move.status === 'SubmittedToInstitution'
  const [date, setDate] = useState(todayIso)
  const [note, setNote] = useState('')
  const [reference, setReference] = useState(detail.summary.institutionReference ?? '')
  const [portalUrl, setPortalUrl] = useState(detail.portalUrl ?? '')
  const [proceed, setProceed] = useState(false)
  const outstanding = move.status === 'ReadyForReview' ? detail.documentGaps.missing : move.status === 'SubmittedToInstitution' ? detail.documentGaps.notAccepted : 0
  const save = useMutation({
    mutationFn: () =>
      post<AdmissionDetail>(`/api/admissions/${detail.summary.id}/status`, {
        acknowledgeDocumentGaps: proceed,
        toStatus: move.status,
        occurredOn: date || null,
        note: note.trim() || null,
        institutionReference: submitting ? reference.trim() || null : null,
        portalUrl: submitting ? portalUrl.trim() || null : null,
      }),
    onSuccess: (d) => {
      toast.success(`Recorded: ${statusLabel[move.status]}.`)
      onDone(d)
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Record: {statusLabel[move.status]}</DialogTitle>
          <DialogDescription>From {statusLabel[detail.summary.status]}. This is added to the history.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          {submitting && !detail.formSubmitted && (
            <Alert variant="destructive">
              <AlertTriangle />
              <AlertTitle>The candidate’s form isn’t signed yet</AlertTitle>
              <AlertDescription>Their Nichotan application form must be signed and submitted before this can go to an institution.</AlertDescription>
            </Alert>
          )}
          {outstanding > 0 && (
            <Alert variant="destructive">
              <AlertTriangle />
              <AlertTitle>
                {outstanding} required document{outstanding === 1 ? ' is' : 's are'} {move.status === 'ReadyForReview' ? 'still missing' : 'not yet accepted'}
              </AlertTitle>
              <AlertDescription>
                <div className="mt-2 flex items-start gap-2">
                  <Checkbox id="proceed" checked={proceed} onCheckedChange={(v) => setProceed(v === true)} className="mt-0.5" />
                  <Label htmlFor="proceed" className="leading-snug font-normal">
                    Continue anyway. This is noted in the history.
                  </Label>
                </div>
              </AlertDescription>
            </Alert>
          )}
          <Field label={submitting ? 'Date submitted on the school’s portal' : 'Date it happened'} type="date" max={todayIso()} value={date} onChange={(e) => setDate(e.target.value)} hint="Use the day it happened, even if you’re recording it later." />
          {submitting && (
            <>
              <Field label="School’s reference (optional)" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
              <Field label="Link on the school’s portal (optional)" type="url" placeholder="https://" value={portalUrl} onChange={(e) => setPortalUrl(e.target.value)} maxLength={500} />
            </>
          )}
          <div className="grid gap-1.5">
            <Label htmlFor="move-note">Note{move.noteRequired ? <span className="text-destructive"> *</span> : ' (optional)'}</Label>
            <textarea id="move-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
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

function CorrectDialog({ detail, onClose, onDone }: { detail: AdmissionDetail; onClose: () => void; onDone: (d: AdmissionDetail) => void }) {
  const [to, setTo] = useState('')
  const [date, setDate] = useState(todayIso)
  const [note, setNote] = useState('')
  const save = useMutation({
    mutationFn: () => post<AdmissionDetail>(`/api/admissions/${detail.summary.id}/correction`, { toStatus: to, occurredOn: date || null, note: note.trim() }),
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
          <SelectField label="Correct status" required options={allStatuses.filter((s) => s !== detail.summary.status).map((s) => ({ value: s, label: statusLabel[s] }))} value={to} onChange={(e) => setTo(e.target.value)} />
          <Field label="Date it applies from" type="date" max={todayIso()} value={date} onChange={(e) => setDate(e.target.value)} />
          <div className="grid gap-1.5">
            <Label htmlFor="correct-note">What is being corrected? <span className="text-destructive">*</span></Label>
            <textarea id="correct-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
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

function DetailsDialog({ detail, onClose, onDone }: { detail: AdmissionDetail; onClose: () => void; onDone: (d: AdmissionDetail) => void }) {
  const [reference, setReference] = useState(detail.summary.institutionReference ?? '')
  const [portalUrl, setPortalUrl] = useState(detail.portalUrl ?? '')
  const save = useMutation({
    mutationFn: () => put<AdmissionDetail>(`/api/admissions/${detail.summary.id}/details`, { institutionReference: reference.trim() || null, portalUrl: portalUrl.trim() || null }),
    onSuccess: (d) => {
      toast.success('School details saved.')
      onDone(d)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>School details</DialogTitle>
          <DialogDescription>The school’s own reference and the link to this application on their portal. Never enter a password.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="School’s reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} />
          <Field label="Link on the school’s portal" type="url" placeholder="https://" value={portalUrl} onChange={(e) => setPortalUrl(e.target.value)} maxLength={500} />
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

// ─────────────────────────── On a candidate's record ───────────────────────────

export function CandidateAdmissionsCard({ candidateId }: { candidateId: string }) {
  const { canManage } = useRoles()
  const [adding, setAdding] = useState(false)
  const list = useQuery({ queryKey: ['admissions', 'candidate', candidateId], queryFn: () => get<AdmissionSummary[]>(`/api/admissions/candidate/${candidateId}`) })

  return (
    <>
      <Card>
        <CardHeader className="flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-ink">Admission applications</CardTitle>
            <CardDescription>One for each institution and programme this candidate is applying to.</CardDescription>
          </div>
          {canManage && (
            <Button variant="outline" size="sm" onClick={() => setAdding(true)}>
              <Plus /> Add application
            </Button>
          )}
        </CardHeader>
        <CardContent>
          {list.isError ? (
            <FormError message="We couldn’t load the applications." />
          ) : !list.data ? (
            <Skeleton className="h-20" />
          ) : list.data.length === 0 ? (
            <p className="py-4 text-center text-sm text-muted-foreground">No applications yet.</p>
          ) : (
            <ul className="grid gap-2">
              {list.data.map((a) => (
                <li key={a.id}>
                  <Link to={`/staff/admissions/${a.id}`} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2.5 hover:bg-accent/40">
                    <span className="min-w-0 text-sm">
                      <span className="font-medium text-ink">{a.institutionName}</span>
                      <span className="block text-xs text-muted-foreground">
                        {a.programmeName} · {intakeText(a.intakeTerm, a.intakeYear)}
                      </span>
                    </span>
                    <AdmissionStatusBadge status={a.status} />
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      {adding && <AddApplicationDialog candidateId={candidateId} onClose={() => setAdding(false)} />}
    </>
  )
}

function AddApplicationDialog({ candidateId, onClose }: { candidateId: string; onClose: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [institutionId, setInstitutionId] = useState('')
  const [programmeId, setProgrammeId] = useState('')
  const [intake, setIntake] = useState('')
  const institutions = useQuery({ queryKey: ['institutions', '', '', false], queryFn: () => get<Institution[]>('/api/institutions') })
  const programmes = useQuery({
    queryKey: ['programmes', 'active', institutionId],
    queryFn: () => get<Programme[]>(`/api/programmes${query({ institutionId })}`),
    enabled: institutionId !== '',
  })
  const programme = programmes.data?.find((p) => p.id === programmeId)

  const create = useMutation({
    mutationFn: () => {
      const [term, year] = intake.split('|')
      return post<AdmissionDetail>('/api/admissions', { candidateId, programmeId, intakeTerm: term, intakeYear: Number(year) })
    },
    onSuccess: (d) => {
      toast.success('Application added.')
      void queryClient.invalidateQueries({ queryKey: ['admissions'] })
      navigate(`/staff/admissions/${d.summary.id}`)
    },
  })

  return (
    <Dialog open onOpenChange={(o) => !o && !create.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add an application</DialogTitle>
          <DialogDescription>Choose the institution, programme and intake. Add them to the directory first if they’re missing.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <SelectField
            label="Institution"
            required
            options={(institutions.data ?? []).map((i) => ({ value: i.id, label: `${i.name} (${i.country})` }))}
            value={institutionId}
            onChange={(e) => {
              setInstitutionId(e.target.value)
              setProgrammeId('')
              setIntake('')
            }}
          />
          <SelectField
            label="Programme"
            required
            disabled={!institutionId}
            options={(programmes.data ?? []).map((p) => ({ value: p.id, label: `${p.name} · ${applicantLevelLabel[p.level]}` }))}
            value={programmeId}
            onChange={(e) => {
              setProgrammeId(e.target.value)
              setIntake('')
            }}
          />
          <SelectField
            label="Intake"
            required
            disabled={!programme}
            placeholder={programme && programme.intakes.length === 0 ? 'No intakes listed' : 'Select…'}
            options={(programme?.intakes ?? []).map((i) => ({ value: `${i.term}|${i.year}`, label: intakeText(i.term, i.year) }))}
            value={intake}
            onChange={(e) => setIntake(e.target.value)}
          />
          <FormError message={create.error ? message(create.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button onClick={() => create.mutate()} disabled={!intake || create.isPending}>
            Add application
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
