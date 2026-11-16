import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Pencil, Plus } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, get, post, put } from '@/lib/api'
import { applicantLevelLabel, destinations, type ApplicantLevel, type DestinationCountry } from '@/lib/application'
import type { Institution } from '@/lib/admissions'
import { PreDepartureTemplateCard } from '@/pages/staff/PreDeparture'
import type { ChecklistStage, DocumentScope, DocumentTypeDto, Requirement } from '@/lib/documents'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const levels: ApplicantLevel[] = ['HighSchool', 'Undergraduate', 'Postgraduate']

/** Administrators: what documents exist and which applications need them. Changes apply to new applications; staff refresh older ones. */
export function Checklists() {
  const queryClient = useQueryClient()
  const types = useQuery({ queryKey: ['document-types'], queryFn: () => get<DocumentTypeDto[]>('/api/document-types') })
  const lines = useQuery({ queryKey: ['document-requirements'], queryFn: () => get<Requirement[]>('/api/document-requirements') })
  const [editingType, setEditingType] = useState<DocumentTypeDto | 'new' | null>(null)
  const [editingLine, setEditingLine] = useState<Requirement | 'new' | null>(null)
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['document-types'] })
    void queryClient.invalidateQueries({ queryKey: ['document-requirements'] })
  }

  return (
    <>
      <PageHeader title="Document checklists" description="The documents Nichotan asks for, and which applications need them. A new application copies the current rules; existing ones change only when staff refresh them." />
      <div className="grid gap-6">
        <Card>
          <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
            <div>
              <CardTitle className="text-lg text-ink">Kinds of document</CardTitle>
              <CardDescription>“Shared” documents are uploaded once for all of a candidate’s applications; “per application” ones are uploaded for each school.</CardDescription>
            </div>
            <Button variant="outline" size="sm" onClick={() => setEditingType('new')}>
              <Plus /> Add a document
            </Button>
          </CardHeader>
          <CardContent>
            {!types.data ? (
              <Skeleton className="h-40" />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Document</TableHead>
                    <TableHead>Used</TableHead>
                    <TableHead className="text-right">
                      <span className="sr-only">Edit</span>
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {types.data.map((t) => (
                    <TableRow key={t.id} className={t.isActive ? undefined : 'opacity-60'}>
                      <TableCell>
                        <span className="font-medium text-ink">{t.name}</span>
                        {t.tracksExpiry && <Badge variant="outline" className="ml-2">Has expiry</Badge>}
                        {t.isAutomatic && <Badge variant="secondary" className="ml-2">Met by the signed form</Badge>}
                        {!t.isActive && <Badge variant="outline" className="ml-2">Retired</Badge>}
                        {t.purpose !== 'None' && <Badge variant="secondary" className="ml-2">Managed on the application</Badge>}
                        {t.description && <p className="text-xs text-muted-foreground">{t.description}</p>}
                      </TableCell>
                      <TableCell>{t.scope === 'Candidate' ? 'Shared' : 'Per application'}</TableCell>
                      <TableCell className="text-right">
                        {!t.isAutomatic && t.purpose === 'None' && (
                          <Button variant="ghost" size="icon" aria-label={`Edit ${t.name}`} onClick={() => setEditingType(t)}>
                            <Pencil />
                          </Button>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
            <div>
              <CardTitle className="text-lg text-ink">Who needs what</CardTitle>
              <CardDescription>Leave country, level or institution empty to mean “any”. When lines overlap, the more specific one wins: institution, then country, then level.</CardDescription>
            </div>
            <Button variant="outline" size="sm" onClick={() => setEditingLine('new')}>
              <Plus /> Add a rule
            </Button>
          </CardHeader>
          <CardContent>
            {!lines.data ? (
              <Skeleton className="h-40" />
            ) : lines.data.length === 0 ? (
              <p className="py-6 text-center text-sm text-muted-foreground">No rules yet.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Document</TableHead>
                    <TableHead>Checklist</TableHead>
                    <TableHead>Country</TableHead>
                    <TableHead>Level</TableHead>
                    <TableHead>Institution</TableHead>
                    <TableHead>Rule</TableHead>
                    <TableHead className="text-right">
                      <span className="sr-only">Edit</span>
                    </TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {lines.data.map((r) => (
                    <TableRow key={r.id} className={r.isActive ? undefined : 'opacity-60'}>
                      <TableCell className="font-medium text-ink">{r.documentTypeName}</TableCell>
                      <TableCell>
                        <Badge variant={r.stage === 'Visa' ? 'secondary' : 'outline'}>{r.stage === 'Visa' ? 'Visa' : 'Admission'}</Badge>
                      </TableCell>
                      <TableCell>{r.country ?? 'Any'}</TableCell>
                      <TableCell>{r.level ? applicantLevelLabel[r.level] : 'Any'}</TableCell>
                      <TableCell>{r.institutionName ?? 'Any'}</TableCell>
                      <TableCell>
                        <Badge variant={r.isRequired ? 'default' : 'outline'}>{r.isRequired ? 'Required' : 'Not required'}</Badge>
                        {!r.isActive && <Badge variant="outline" className="ml-1">Off</Badge>}
                      </TableCell>
                      <TableCell className="text-right">
                        <Button variant="ghost" size="icon" aria-label={`Edit rule for ${r.documentTypeName}`} onClick={() => setEditingLine(r)}>
                          <Pencil />
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
        <PreDepartureTemplateCard />
      </div>
      {editingType && <TypeDialog target={editingType} onClose={() => setEditingType(null)} onSaved={refresh} />}
      {editingLine && types.data && <LineDialog target={editingLine} types={types.data.filter((t) => t.isActive && t.purpose === 'None')} onClose={() => setEditingLine(null)} onSaved={refresh} />}
    </>
  )
}

function TypeDialog({ target, onClose, onSaved }: { target: DocumentTypeDto | 'new'; onClose: () => void; onSaved: () => void }) {
  const existing = target === 'new' ? null : target
  const [name, setName] = useState(existing?.name ?? '')
  const [description, setDescription] = useState(existing?.description ?? '')
  const [scope, setScope] = useState<DocumentScope>(existing?.scope ?? 'Candidate')
  const [expiry, setExpiry] = useState(existing?.tracksExpiry ?? false)
  const [active, setActive] = useState(existing?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), description: description.trim() || null, scope, tracksExpiry: expiry, isActive: active }
      return existing ? put<DocumentTypeDto>(`/api/document-types/${existing.id}`, body) : post<DocumentTypeDto>('/api/document-types', body)
    },
    onSuccess: () => {
      toast.success('Saved.')
      onSaved()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{existing ? `Edit ${existing.name}` : 'Add a document'}</DialogTitle>
          <DialogDescription>Add it to the checklist afterwards with a rule below.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="Name" required value={name} onChange={(e) => setName(e.target.value)} maxLength={150} />
          <Field label="Help text for candidates" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={1000} />
          <SelectField
            label="Uploaded"
            disabled={existing !== null}
            placeholder="Choose…"
            options={[
              { value: 'Candidate', label: 'Once, shared by all applications' },
              { value: 'Application', label: 'Separately for each application' },
            ]}
            value={scope}
            onChange={(e) => setScope(e.target.value as DocumentScope)}
          />
          <div className="flex items-center gap-2">
            <Checkbox id="expiry" checked={expiry} onCheckedChange={(v) => setExpiry(v === true)} />
            <Label htmlFor="expiry" className="font-normal">
              It has an expiry date (like a passport)
            </Label>
          </div>
          {existing && (
            <div className="flex items-center gap-2">
              <Checkbox id="active" checked={active} onCheckedChange={(v) => setActive(v === true)} />
              <Label htmlFor="active" className="font-normal">
                In use
              </Label>
            </div>
          )}
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!name.trim() || save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function LineDialog({ target, types, onClose, onSaved }: { target: Requirement | 'new'; types: DocumentTypeDto[]; onClose: () => void; onSaved: () => void }) {
  const existing = target === 'new' ? null : target
  const institutions = useQuery({ queryKey: ['institutions', '', '', false], queryFn: () => get<Institution[]>('/api/institutions') })
  const [typeId, setTypeId] = useState(existing?.documentTypeId ?? '')
  const [country, setCountry] = useState<DestinationCountry | ''>(existing?.country ?? '')
  const [level, setLevel] = useState<ApplicantLevel | ''>(existing?.level ?? '')
  const [institutionId, setInstitutionId] = useState(existing?.institutionId ?? '')
  const [stage, setStage] = useState<ChecklistStage>(existing?.stage ?? 'Admission')
  const [required, setRequired] = useState(existing?.isRequired ?? true)
  const [active, setActive] = useState(existing?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => {
      const body = { documentTypeId: typeId, country: country || null, level: level || null, institutionId: institutionId || null, isRequired: required, isActive: active, stage }
      return existing ? put<Requirement>(`/api/document-requirements/${existing.id}`, body) : post<Requirement>('/api/document-requirements', body)
    },
    onSuccess: () => {
      toast.success('Saved. New applications will use it.')
      onSaved()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{existing ? 'Edit rule' : 'Add a rule'}</DialogTitle>
          <DialogDescription>“Not required” is useful to switch off a broader rule for one institution.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <SelectField label="Document" required disabled={existing !== null} options={types.map((t) => ({ value: t.id, label: t.name }))} value={typeId} onChange={(e) => setTypeId(e.target.value)} />
          <SelectField label="Checklist" disabled={existing !== null} options={[{ value: 'Admission', label: 'Admission (applying to the school)' }, { value: 'Visa', label: 'Visa (applying for the visa)' }]} value={stage} onChange={(e) => setStage(e.target.value as ChecklistStage)} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Country" placeholder="Any country" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value as DestinationCountry)} />
            <SelectField label="Applicant level" placeholder="Any level" options={levels.map((l) => ({ value: l, label: applicantLevelLabel[l] }))} value={level} onChange={(e) => setLevel(e.target.value as ApplicantLevel)} />
          </div>
          <SelectField label="Institution" placeholder="Any institution" options={(institutions.data ?? []).map((i) => ({ value: i.id, label: `${i.name} (${i.country})` }))} value={institutionId} onChange={(e) => setInstitutionId(e.target.value)} />
          <div className="flex items-center gap-2">
            <Checkbox id="line-required" checked={required} onCheckedChange={(v) => setRequired(v === true)} />
            <Label htmlFor="line-required" className="font-normal">
              Required (untick for “not required”)
            </Label>
          </div>
          {existing && (
            <div className="flex items-center gap-2">
              <Checkbox id="line-active" checked={active} onCheckedChange={(v) => setActive(v === true)} />
              <Label htmlFor="line-active" className="font-normal">
                Rule is on
              </Label>
            </div>
          )}
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!typeId || save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
