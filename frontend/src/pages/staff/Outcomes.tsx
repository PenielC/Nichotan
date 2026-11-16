import { useRef, useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Pencil, Plus, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, download, get, postForm, put } from '@/lib/api'
import { admissionsRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { dayText, todayIso, type DocRef, type Outcome } from '@/lib/admissions'
import type { DocumentPurpose, DocumentTypeDto } from '@/lib/documents'
import { acceptedTypes, maxUploadBytes } from '@/lib/documents'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

/**
 * What came back from the school after the application: its offer and the formal acceptance letter (with the
 * PAL number where one applies). Three separate records, entered by staff. A receipt being uploaded never verifies a payment.
 */
export function OutcomeCard({ applicationId, candidateId, status }: { applicationId: string; candidateId: string; status: string }) {
  const { state } = useAuth()
  const canManage = state.status === 'signedIn' && hasAnyRole(state.session, admissionsRoles)
  const queryClient = useQueryClient()
  const outcome = useQuery({ queryKey: ['admissions', 'outcome', applicationId], queryFn: () => get<Outcome>(`/api/admissions/${applicationId}/outcome`) })
  const types = useQuery({ queryKey: ['document-types'], queryFn: () => get<DocumentTypeDto[]>('/api/document-types') })
  const [dialog, setDialog] = useState<'offer' | 'acceptance' | null>(null)

  const update = (o: Outcome) => {
    queryClient.setQueryData(['admissions', 'outcome', applicationId], o)
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'list'] })
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'stats'] })
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'pal-follow-ups'] })
  }
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'outcome', applicationId] })
    void queryClient.invalidateQueries({ queryKey: ['admissions', 'pal-follow-ups'] })
  }

  const o = outcome.data
  if (!o || (!o.offer && status !== 'OfferReceived' && !o.offerLetter)) return outcome.isLoading ? <Skeleton className="h-40" /> : null
  const typeId = (purpose: DocumentPurpose) => types.data?.find((t) => t.purpose === purpose)?.id

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg text-ink">Offer and acceptance</CardTitle>
          <CardDescription>Two separate steps, recorded from what the school sends. Nothing here is checked against the school automatically.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-6">
          {o.palApplies && o.acceptance && !o.palLetter && !o.acceptance.palNumber && (
            <div role="status" className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
              <p className="font-medium">Follow up on the PAL</p>
              <p>The acceptance letter arrived on {dayText(o.acceptance.receivedOn)} but the school hasn’t sent the PAL yet. Chase the school, then add the PAL number or upload the letter below.</p>
            </div>
          )}
          {/* The offer */}
          <Section
            title="1. Offer"
            action={canManage && <Button variant="ghost" size="sm" onClick={() => setDialog('offer')}>{o.offer ? <><Pencil /> Edit</> : <><Plus /> Record the offer</>}</Button>}
          >
            {o.offer ? (
              <>
                <Fact label="Conditions" value={o.offer.conditions ?? 'None given'} />
                <Fact label="Reply by" value={dayText(o.offer.respondByOn)} />
                <Letter label="Offer letter" doc={o.offerLetter} typeId={typeId('OfferLetter')} applicationId={applicationId} candidateId={candidateId} canUpload={canManage} onUploaded={refresh} />
              </>
            ) : (
              <>
                <p className="text-sm text-muted-foreground">{o.offerLetter ? 'The student has uploaded their offer letter. Read it, record the application as Offer Received, then add the details here.' : 'Add the offer’s details first. The student is emailed a link when you do.'}</p>
                {o.offerLetter && <Letter label="Offer letter" doc={o.offerLetter} typeId={typeId('OfferLetter')} applicationId={applicationId} candidateId={candidateId} canUpload={false} onUploaded={refresh} />}
              </>
            )}
          </Section>

          {/* The formal acceptance */}
          {o.offer && (
            <Section
              title="2. Formal acceptance letter"
              action={canManage && <Button variant="ghost" size="sm" onClick={() => setDialog('acceptance')}>{o.acceptance ? <><Pencil /> Edit</> : <><Plus /> Record it</>}</Button>}
            >
              {o.acceptance ? (
                <>
                  <Fact label="Received" value={dayText(o.acceptance.receivedOn)} />
                  {o.palApplies && <Fact label="PAL number" value={o.acceptance.palNumber ?? 'Not given'} />}
                  {o.acceptance.note && <Fact label="Note" value={o.acceptance.note} />}
                  <Letter label="Acceptance letter" doc={o.acceptanceLetter} typeId={typeId('AcceptanceLetter')} applicationId={applicationId} candidateId={candidateId} canUpload={canManage} onUploaded={refresh} />
                  {o.palApplies && <Letter label="PAL letter" doc={o.palLetter} typeId={typeId('PalLetter')} applicationId={applicationId} candidateId={candidateId} canUpload={canManage} onUploaded={refresh} />}
                </>
              ) : (
                <p className="text-sm text-muted-foreground">Not received yet. Record it when the school sends its formal acceptance letter{o.palApplies ? ', along with the PAL number if there is one' : ''}.</p>
              )}
            </Section>
          )}
        </CardContent>
      </Card>

      {dialog === 'offer' && <OfferDialog applicationId={applicationId} outcome={o} onClose={() => setDialog(null)} onDone={update} />}
      {dialog === 'acceptance' && <AcceptanceDialog applicationId={applicationId} outcome={o} onClose={() => setDialog(null)} onDone={update} />}
    </>
  )
}

function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="grid gap-2 rounded-xl border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-semibold text-ink">{title}</h3>
        {action}
      </div>
      {children}
    </section>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <p className="text-sm">
      <span className="text-muted-foreground">{label}: </span>
      <span className="text-ink">{value}</span>
    </p>
  )
}

/** A stored file: download it, or (for staff) add or replace it. Choosing a file uploads it straight away. */
export function Letter({ label, doc, typeId, applicationId, candidateId, canUpload, onUploaded, hint }: { label: string; doc: DocRef | null; typeId?: string; applicationId: string; candidateId: string; canUpload: boolean; onUploaded: () => void; hint?: string }) {
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const choose = async (file: File | undefined) => {
    if (!file || !typeId) return
    if (file.size > maxUploadBytes) return void toast.error('That file is over 10 MB.')
    setBusy(true)
    try {
      const form = new FormData()
      form.set('DocumentTypeId', typeId)
      form.set('ApplicationId', applicationId)
      form.set('CandidateId', candidateId)
      form.set('File', file)
      await postForm('/api/documents', form)
      toast.success(`${label} uploaded.`)
      onUploaded()
    } catch (e) {
      toast.error(message(e))
    } finally {
      setBusy(false)
      if (input.current) input.current.value = ''
    }
  }
  return (
    <div className="flex flex-wrap items-center gap-2 text-sm">
      <span className="text-muted-foreground">{label}:</span>
      {doc ? (
        <button type="button" className="inline-flex items-center gap-1 text-primary hover:underline" onClick={() => download(`/api/documents/${doc.documentId}/file`, doc.fileName).catch(() => toast.error('We couldn’t download the file.'))}>
          <Download className="size-3.5" /> {doc.fileName}
        </button>
      ) : (
        <span className="text-muted-foreground">{hint ?? 'Not uploaded'}</span>
      )}
      {canUpload && typeId && (
        <>
          <input ref={input} type="file" accept={acceptedTypes} className="sr-only" aria-label={`Choose the ${label.toLowerCase()} file`} onChange={(e) => void choose(e.target.files?.[0])} />
          <Button variant="outline" size="sm" disabled={busy} onClick={() => input.current?.click()}>
            <Upload /> {doc ? 'Replace' : 'Upload'}
          </Button>
        </>
      )}
    </div>
  )
}

function OfferDialog({ applicationId, outcome, onClose, onDone }: { applicationId: string; outcome: Outcome; onClose: () => void; onDone: (o: Outcome) => void }) {
  const [conditions, setConditions] = useState(outcome.offer?.conditions ?? '')
  const [respondBy, setRespondBy] = useState(outcome.offer?.respondByOn ?? '')
  const save = useMutation({
    mutationFn: () => put<Outcome>(`/api/admissions/${applicationId}/outcome/offer`, { conditions: conditions.trim() || null, respondByOn: respondBy || null }),
    onSuccess: (o) => {
      toast.success(outcome.offer ? 'Offer updated.' : 'Offer recorded. The student has been emailed.')
      onDone(o)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(v) => !v && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{outcome.offer ? 'Edit the offer' : 'Record the offer'}</DialogTitle>
          <DialogDescription>Copy the details from the school’s offer. You can add the letter itself after saving.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <div className="grid gap-1.5">
            <Label htmlFor="offer-conditions">Conditions (if any)</Label>
            <textarea id="offer-conditions" value={conditions} onChange={(e) => setConditions(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" placeholder="e.g. Final transcript with 70% in Mathematics" />
          </div>
          <Field label="Reply by (if the school gives a date)" type="date" value={respondBy} onChange={(e) => setRespondBy(e.target.value)} />
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

function AcceptanceDialog({ applicationId, outcome, onClose, onDone }: { applicationId: string; outcome: Outcome; onClose: () => void; onDone: (o: Outcome) => void }) {
  const [receivedOn, setReceivedOn] = useState(outcome.acceptance?.receivedOn ?? todayIso())
  const [pal, setPal] = useState(outcome.acceptance?.palNumber ?? '')
  const [note, setNote] = useState(outcome.acceptance?.note ?? '')
  const save = useMutation({
    mutationFn: () => put<Outcome>(`/api/admissions/${applicationId}/outcome/acceptance`, { receivedOn, palNumber: outcome.palApplies ? pal.trim() || null : null, note: note.trim() || null }),
    onSuccess: (o) => {
      toast.success('Acceptance recorded. You can now add the letter.')
      onDone(o)
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(v) => !v && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{outcome.acceptance ? 'Edit the acceptance' : 'Record the formal acceptance letter'}</DialogTitle>
          <DialogDescription>This is separate from the offer: the school’s formal acceptance letter has arrived.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="Date it arrived" required type="date" max={todayIso()} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} />
          {outcome.palApplies && <Field label="PAL number (optional)" value={pal} onChange={(e) => setPal(e.target.value)} maxLength={60} hint="The Provincial Attestation Letter number that came with the acceptance, if there is one." />}
          <div className="grid gap-1.5">
            <Label htmlFor="acceptance-note">Note</Label>
            <textarea id="acceptance-note" value={note} onChange={(e) => setNote(e.target.value)} rows={2} maxLength={1000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
          </div>
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!receivedOn || save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
