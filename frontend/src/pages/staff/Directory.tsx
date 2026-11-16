import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, ArchiveRestore, ExternalLink, Pencil, Plus, Search, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, get, post, put, query } from '@/lib/api'
import { applicantLevelLabel, destinations, startTerms, type ApplicantLevel, type DestinationCountry, type StartTerm } from '@/lib/application'
import { admissionsRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { intakeText, type Institution, type Intake, type Programme, type Suggestion } from '@/lib/admissions'
import { formatDate } from '@/lib/format'

const levels: ApplicantLevel[] = ['HighSchool', 'Undergraduate', 'Postgraduate']
const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

function useCanManage() {
  const { state } = useAuth()
  return state.status === 'signedIn' && hasAnyRole(state.session, admissionsRoles)
}

export function Directory() {
  const canManage = useCanManage()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [country, setCountry] = useState('')
  const [showArchived, setShowArchived] = useState(false)
  const [editing, setEditing] = useState<Institution | 'new' | null>(null)
  const [programmesOf, setProgrammesOf] = useState<Institution | null>(null)

  const institutions = useQuery({
    queryKey: ['institutions', search, country, showArchived],
    queryFn: () => get<Institution[]>(`/api/institutions${query({ search, country, includeArchived: showArchived ? 'true' : undefined })}`),
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['institutions'] })
  const archive = useMutation({
    mutationFn: (i: Institution) => post<Institution>(`/api/institutions/${i.id}/archive`, { archived: !i.isArchived }),
    onSuccess: (i) => {
      toast.success(i.isArchived ? `${i.name} archived.` : `${i.name} restored.`)
      refresh()
    },
    onError: (e) => toast.error(message(e)),
  })

  return (
    <>
      <PageHeader
        title="Institutions and programmes"
        description="The universities and colleges Nichotan applies to for candidates. Archived entries stay on existing applications."
        actions={
          canManage && (
            <Button onClick={() => setEditing('new')}>
              <Plus /> Add institution
            </Button>
          )
        }
      />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="relative min-w-60 flex-1 sm:max-w-md">
              <span className="sr-only">Search institutions</span>
              <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search by name" className="h-10 pl-9" maxLength={100} />
            </label>
            <SelectField label="Country" className="w-40" placeholder="All countries" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value)} />
            <div className="flex items-center gap-2 pb-2.5">
              <Checkbox id="archived" checked={showArchived} onCheckedChange={(v) => setShowArchived(v === true)} />
              <Label htmlFor="archived" className="font-normal">
                Show archived
              </Label>
            </div>
          </div>

          {institutions.isError ? (
            <FormError message="We couldn’t load the directory. Please refresh the page." />
          ) : !institutions.data ? (
            <Skeleton className="h-64" />
          ) : institutions.data.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search || country ? 'No institutions match.' : canManage ? 'No institutions yet. Add the first one.' : 'No institutions have been added yet.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Institution</TableHead>
                  <TableHead>Country</TableHead>
                  <TableHead className="text-right">Programmes</TableHead>
                  <TableHead className="text-right">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {institutions.data.map((i) => (
                  <TableRow key={i.id} className={i.isArchived ? 'opacity-60' : undefined}>
                    <TableCell>
                      <div className="flex flex-wrap items-center gap-2 font-medium text-ink">
                        {i.name}
                        {i.isArchived && <Badge variant="outline">Archived</Badge>}
                        {i.website && (
                          <a href={i.website} target="_blank" rel="noreferrer noopener" aria-label={`${i.name} website`} className="text-muted-foreground hover:text-foreground">
                            <ExternalLink className="size-3.5" />
                          </a>
                        )}
                      </div>
                      {i.notes && <p className="mt-0.5 line-clamp-1 text-xs text-muted-foreground">{i.notes}</p>}
                    </TableCell>
                    <TableCell>{i.country}</TableCell>
                    <TableCell className="text-right">{i.programmeCount}</TableCell>
                    <TableCell className="text-right whitespace-nowrap">
                      <Button variant="outline" size="sm" onClick={() => setProgrammesOf(i)}>
                        Programmes
                      </Button>
                      {canManage && (
                        <>
                          <Button variant="ghost" size="icon" aria-label={`Edit ${i.name}`} onClick={() => setEditing(i)}>
                            <Pencil />
                          </Button>
                          <Button variant="ghost" size="icon" aria-label={i.isArchived ? `Restore ${i.name}` : `Archive ${i.name}`} onClick={() => archive.mutate(i)} disabled={archive.isPending}>
                            {i.isArchived ? <ArchiveRestore /> : <Archive />}
                          </Button>
                        </>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <SuggestionsCard canManage={canManage} />
      <InstitutionDialog target={editing} onClose={() => setEditing(null)} onSaved={refresh} />
      <ProgrammesDialog institution={programmesOf} onClose={() => setProgrammesOf(null)} canManage={canManage} onChanged={refresh} />
    </>
  )
}

function InstitutionDialog({ target, onClose, onSaved }: { target: Institution | 'new' | null; onClose: () => void; onSaved: () => void }) {
  const open = target !== null
  const existing = target && target !== 'new' ? target : null
  return (
    <Dialog open={open} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>{open && <InstitutionForm key={existing?.id ?? 'new'} existing={existing} onClose={onClose} onSaved={onSaved} />}</DialogContent>
    </Dialog>
  )
}

function InstitutionForm({ existing, onClose, onSaved }: { existing: Institution | null; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(existing?.name ?? '')
  const [country, setCountry] = useState<DestinationCountry | ''>(existing?.country ?? '')
  const [website, setWebsite] = useState(existing?.website ?? '')
  const [notes, setNotes] = useState(existing?.notes ?? '')
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), country, website: website.trim() || null, notes: notes.trim() || null }
      return existing ? put<Institution>(`/api/institutions/${existing.id}`, body) : post<Institution>('/api/institutions', body)
    },
    onSuccess: () => {
      toast.success(existing ? 'Institution updated.' : 'Institution added.')
      onSaved()
      onClose()
    },
  })

  return (
    <>
      <DialogHeader>
        <DialogTitle>{existing ? 'Edit institution' : 'Add institution'}</DialogTitle>
        <DialogDescription>A university or college candidates can apply to.</DialogDescription>
      </DialogHeader>
      <div className="grid gap-4">
        <Field label="Name" required value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
        <SelectField label="Country" required options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value as DestinationCountry)} />
        <Field label="Website" type="url" placeholder="https://" value={website} onChange={(e) => setWebsite(e.target.value)} maxLength={500} />
        <div className="grid gap-1.5">
          <Label htmlFor="inst-notes">Notes</Label>
          <textarea id="inst-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
        </div>
        <FormError message={save.error ? message(save.error) : null} />
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={save.isPending}>
          Cancel
        </Button>
        <Button onClick={() => save.mutate()} disabled={!name.trim() || !country || save.isPending}>
          Save
        </Button>
      </DialogFooter>
    </>
  )
}

// ─────────────────────────── Programmes ───────────────────────────

function ProgrammesDialog({ institution, onClose, canManage, onChanged }: { institution: Institution | null; onClose: () => void; canManage: boolean; onChanged: () => void }) {
  return (
    <Dialog open={institution !== null} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-2xl">{institution && <ProgrammesPanel institution={institution} canManage={canManage} onChanged={onChanged} />}</DialogContent>
    </Dialog>
  )
}

function ProgrammesPanel({ institution, canManage, onChanged }: { institution: Institution; canManage: boolean; onChanged: () => void }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Programme | 'new' | null>(null)
  const programmes = useQuery({
    queryKey: ['programmes', institution.id],
    queryFn: () => get<Programme[]>(`/api/programmes${query({ institutionId: institution.id, includeArchived: 'true' })}`),
  })
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['programmes'] })
    onChanged()
  }
  const archive = useMutation({
    mutationFn: (p: Programme) => post<Programme>(`/api/programmes/${p.id}/archive`, { archived: !p.isArchived }),
    onSuccess: refresh,
    onError: (e) => toast.error(message(e)),
  })

  if (editing) return <ProgrammeForm institution={institution} existing={editing === 'new' ? null : editing} onDone={() => setEditing(null)} onSaved={refresh} />

  return (
    <>
      <DialogHeader>
        <DialogTitle>{institution.name}</DialogTitle>
        <DialogDescription>
          {institution.country} · programmes and the intakes they start in.
        </DialogDescription>
      </DialogHeader>
      {programmes.isError ? (
        <FormError message="We couldn’t load the programmes." />
      ) : !programmes.data ? (
        <Skeleton className="h-32" />
      ) : programmes.data.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted-foreground">No programmes yet.</p>
      ) : (
        <ul className="grid gap-2">
          {programmes.data.map((p) => (
            <li key={p.id} className={`flex flex-wrap items-start justify-between gap-2 rounded-lg border p-3 ${p.isArchived ? 'opacity-60' : ''}`}>
              <div className="min-w-0">
                <p className="font-medium text-ink">
                  {p.name} <Badge variant="secondary">{applicantLevelLabel[p.level]}</Badge> {p.isArchived && <Badge variant="outline">Archived</Badge>}
                </p>
                <p className="mt-1 text-xs text-muted-foreground">{p.intakes.length ? p.intakes.map((i) => intakeText(i.term, i.year)).join(' · ') : 'No intakes listed'}</p>
              </div>
              {canManage && (
                <div className="flex gap-1">
                  <Button variant="ghost" size="icon" aria-label={`Edit ${p.name}`} onClick={() => setEditing(p)}>
                    <Pencil />
                  </Button>
                  <Button variant="ghost" size="icon" aria-label={p.isArchived ? `Restore ${p.name}` : `Archive ${p.name}`} onClick={() => archive.mutate(p)} disabled={archive.isPending}>
                    {p.isArchived ? <ArchiveRestore /> : <Archive />}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {canManage && (
        <DialogFooter>
          <Button onClick={() => setEditing('new')}>
            <Plus /> Add programme
          </Button>
        </DialogFooter>
      )}
    </>
  )
}

function ProgrammeForm({ institution, existing, onDone, onSaved }: { institution: Institution; existing: Programme | null; onDone: () => void; onSaved: () => void }) {
  const [name, setName] = useState(existing?.name ?? '')
  const [level, setLevel] = useState<ApplicantLevel | ''>(existing?.level ?? '')
  const [intakes, setIntakes] = useState<{ term: StartTerm | ''; year: string }[]>(existing?.intakes.map((i) => ({ term: i.term, year: String(i.year) })) ?? [{ term: '', year: '' }])
  const complete = intakes.filter((i) => i.term && i.year)

  const save = useMutation({
    mutationFn: () => {
      const body = { institutionId: institution.id, name: name.trim(), level, intakes: complete.map((i): Intake => ({ term: i.term as StartTerm, year: Number(i.year) })) }
      return existing ? put<Programme>(`/api/programmes/${existing.id}`, body) : post<Programme>('/api/programmes', body)
    },
    onSuccess: () => {
      toast.success(existing ? 'Programme updated.' : 'Programme added.')
      onSaved()
      onDone()
    },
  })
  const setIntake = (index: number, change: Partial<{ term: StartTerm | ''; year: string }>) => setIntakes((list) => list.map((x, i) => (i === index ? { ...x, ...change } : x)))

  return (
    <>
      <DialogHeader>
        <DialogTitle>{existing ? 'Edit programme' : 'Add programme'}</DialogTitle>
        <DialogDescription>{institution.name}</DialogDescription>
      </DialogHeader>
      <div className="grid gap-4">
        <Field label="Programme name" required value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
        <SelectField label="Level" required options={levels.map((l) => ({ value: l, label: applicantLevelLabel[l] }))} value={level} onChange={(e) => setLevel(e.target.value as ApplicantLevel)} />
        <fieldset className="grid gap-2">
          <legend className="mb-1 text-sm font-medium">Intakes (when it starts)</legend>
          {intakes.map((intake, i) => (
            <div key={i} className="flex items-end gap-2">
              <SelectField label="Term" className="flex-1" options={startTerms} value={intake.term} onChange={(e) => setIntake(i, { term: e.target.value as StartTerm })} />
              <Field label="Year" type="number" inputMode="numeric" className="w-28" value={intake.year} onChange={(e) => setIntake(i, { year: e.target.value })} />
              <Button type="button" variant="ghost" size="icon" aria-label="Remove intake" onClick={() => setIntakes((l) => l.filter((_, k) => k !== i))} disabled={intakes.length === 1}>
                <Trash2 />
              </Button>
            </div>
          ))}
          <Button type="button" variant="outline" size="sm" className="justify-self-start" onClick={() => setIntakes((l) => [...l, { term: '', year: '' }])}>
            <Plus /> Add intake
          </Button>
        </fieldset>
        <FormError message={save.error ? message(save.error) : null} />
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onDone} disabled={save.isPending}>
          Back
        </Button>
        <Button onClick={() => save.mutate()} disabled={!name.trim() || !level || save.isPending}>
          Save
        </Button>
      </DialogFooter>
    </>
  )
}

/** Schools students asked for that aren't listed. Add them above, then mark the request done. */
function SuggestionsCard({ canManage }: { canManage: boolean }) {
  const queryClient = useQueryClient()
  const suggestions = useQuery({ queryKey: ['admissions', 'suggestions', 'staff'], queryFn: () => get<Suggestion[]>('/api/admissions/suggestions?status=New') })
  const resolve = useMutation({
    mutationFn: (v: { id: string; status: 'Added' | 'Declined' }) => put<Suggestion>(`/api/admissions/suggestions/${v.id}`, { status: v.status, staffNote: null }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['admissions', 'suggestions'] }),
    onError: (e) => toast.error(message(e)),
  })
  if (!suggestions.data || suggestions.data.length === 0) return null
  return (
    <Card className="mt-6 border-l-4 border-l-amber-500">
      <CardContent className="grid gap-3">
        <div>
          <p className="font-semibold text-ink">Schools students have asked for ({suggestions.data.length})</p>
          <p className="text-sm text-muted-foreground">Add each one to the list above if it’s right, then mark it added so the student knows.</p>
        </div>
        <ul className="grid gap-2">
          {suggestions.data.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm">
              <span className="min-w-0">
                <span className="font-medium text-ink">{s.institutionName}</span> <span className="text-muted-foreground">({s.country})</span>
                <span className="block text-xs text-muted-foreground">
                  {s.programmeName}
                  {s.intakeTerm && s.intakeYear ? ` · ${intakeText(s.intakeTerm, s.intakeYear)}` : ''} · asked by {s.candidateName} on {formatDate(s.createdAtUtc)}
                </span>
                {s.note && <span className="block text-xs text-ink">“{s.note}”</span>}
              </span>
              {canManage && (
                <span className="flex gap-1">
                  <Button size="sm" onClick={() => resolve.mutate({ id: s.id, status: 'Added' })} disabled={resolve.isPending}>
                    Mark added
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => resolve.mutate({ id: s.id, status: 'Declined' })} disabled={resolve.isPending}>
                    Decline
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}
