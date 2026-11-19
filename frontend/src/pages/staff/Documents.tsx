import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Download, Eye, History, Plus, RefreshCw, Upload, X } from 'lucide-react'
import { toast } from 'sonner'
import { SlotBadge, UploadDialog } from '@/components/document-parts'
import { SlotFileList } from '@/components/slot-files'
import { FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, download, get, post, put, query, type Paged } from '@/lib/api'
import { dayText } from '@/lib/admissions'
import { WhatsAppShareButton } from '@/components/whatsapp-share'
import { hasAnyRole, useAuth, visaRoles } from '@/lib/auth'
import type { ChecklistResult, DocumentDetail, DocumentsOverview, DocumentSlot, DocumentStatus, DocumentTypeDto, ReviewQueueItem } from '@/lib/documents'
import { formatDate, formatDateTime } from '@/lib/format'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')

function useDocumentRoles() {
  const { state } = useAuth()
  const signedIn = state.status === 'signedIn'
  return {
    canReview: signedIn && hasAnyRole(state.session, ['Administrator', 'AdmissionsCounsellor']),
    canManageChecklist: signedIn && hasAnyRole(state.session, ['Administrator', 'AdmissionsCounsellor']),
  }
}

async function saveFile(documentId: string, fileName: string, version?: number) {
  try {
    await download(`/api/documents/${documentId}/file${version ? `?version=${version}` : ''}`, fileName)
  } catch {
    toast.error('We couldn’t download the file. Please try again.')
  }
}

// ─────────────────────────── Review queue ───────────────────────────

export function ReviewQueue() {
  const query1 = useQuery({ queryKey: ['documents', 'queue'], queryFn: () => get<Paged<ReviewQueueItem>>(`/api/documents/review-queue${query({ pageSize: 100 })}`) })
  return (
    <>
      <PageHeader title="Documents to review" description="Uploads waiting for a decision, oldest first." />
      <Card>
        <CardContent>
          {query1.isError ? (
            <FormError message="We couldn’t load the queue. Please refresh the page." />
          ) : !query1.data ? (
            <Skeleton className="h-48" />
          ) : query1.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">Nothing is waiting for review.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Candidate</TableHead>
                  <TableHead>Document</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">Uploaded</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {query1.data.items.map((i) => (
                  <TableRow key={i.documentId}>
                    <TableCell>
                      <Link to={`/staff/candidates/${i.candidateId}`} className="font-medium text-ink hover:underline">
                        {i.candidateName}
                      </Link>
                    </TableCell>
                    <TableCell>
                      {i.typeName}
                      <div className="text-xs text-muted-foreground">
                        {i.fileName}
                        {i.version > 1 ? ` · version ${i.version}` : ''}
                      </div>
                    </TableCell>
                    <TableCell>
                      <SlotBadge status={i.status} />
                    </TableCell>
                    <TableCell className="text-right text-muted-foreground">{formatDate(i.uploadedAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </>
  )
}

// ─────────────────────────── On a candidate's record ───────────────────────────

export function CandidateDocumentsCard({ candidateId }: { candidateId: string }) {
  const { canReview } = useDocumentRoles()
  const queryClient = useQueryClient()
  const overview = useQuery({ queryKey: ['documents', 'candidate', candidateId], queryFn: () => get<DocumentsOverview>(`/api/documents/candidate/${candidateId}`) })
  const [uploading, setUploading] = useState<{ slot: DocumentSlot; position?: number } | null>(null)
  const [reviewing, setReviewing] = useState<DocumentSlot | null>(null)
  const [history, setHistory] = useState<string | null>(null)
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['documents'] })

  const decide = useMutation({
    mutationFn: (v: { id: string; decision: DocumentStatus; note?: string }) => post<DocumentDetail>(`/api/documents/${v.id}/review`, { decision: v.decision, note: v.note?.trim() || null }),
    onSuccess: () => {
      refresh()
      void queryClient.invalidateQueries({ queryKey: ['admissions'] })
    },
    onError: (e) => toast.error(message(e)),
  })

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="text-ink">Documents</CardTitle>
          <CardDescription>What this candidate has been asked for and what they’ve uploaded. Uploading isn’t verifying: each file needs a decision.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-2">
          {overview.isError ? (
            <FormError message="We couldn’t load the documents." />
          ) : !overview.data ? (
            <Skeleton className="h-24" />
          ) : (
            overview.data.slots.map((s) => (
              <div key={`${s.documentTypeId}-${s.applicationId}`} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2.5">
                <div className="min-w-0 text-sm">
                  <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
                    {s.typeName}
                    {!s.required && <Badge variant="outline">Optional</Badge>}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {s.applicationLabel ? `${s.applicationLabel} · ` : ''}
                    {s.isAutomatic ? 'Met when the form is signed' : s.allowsMultipleFiles && s.files.length > 0 ? `${s.files.length} file${s.files.length === 1 ? '' : 's'}` : s.fileName ? `${s.fileName}${s.version > 1 ? ` · v${s.version}` : ''}${s.expiresOn ? ` · expires ${dayText(s.expiresOn)}` : ''}` : 'Nothing uploaded'}
                  </p>
                  {s.reviewNote && <p className="mt-1 text-xs text-destructive">Asked to fix: {s.reviewNote}</p>}
                  {s.allowsMultipleFiles && s.files.length > 0 && <SlotFileList slot={s} canChange={canReview && s.status !== 'Accepted'} onReplace={(position) => setUploading({ slot: s, position })} onChanged={refresh} />}
                </div>
                <div className="flex flex-wrap items-center gap-1">
                  <SlotBadge status={s.status} />
                  {s.documentId && s.fileName && (
                    <>
                      {!s.allowsMultipleFiles && (
                        <Button variant="ghost" size="icon" aria-label={`Download ${s.typeName}`} onClick={() => saveFile(s.documentId!, s.fileName!)}>
                          <Download />
                        </Button>
                      )}
                      <Button variant="ghost" size="icon" aria-label={`History of ${s.typeName}`} onClick={() => setHistory(s.documentId)}>
                        <History />
                      </Button>
                    </>
                  )}
                  {canReview && s.needsReview && s.purpose === 'None' && s.documentId && s.status !== 'NeedsResubmission' && (
                    <>
                      {s.status === 'Uploaded' && (
                        <Button variant="outline" size="sm" onClick={() => decide.mutate({ id: s.documentId!, decision: 'UnderReview' })} disabled={decide.isPending}>
                          <Eye /> Start review
                        </Button>
                      )}
                      {s.status !== 'Accepted' && (
                        <Button size="sm" onClick={() => decide.mutate({ id: s.documentId!, decision: 'Accepted' })} disabled={decide.isPending}>
                          <Check /> Accept
                        </Button>
                      )}
                      <Button variant="outline" size="sm" onClick={() => setReviewing(s)}>
                        <X /> Needs resubmission
                      </Button>
                    </>
                  )}
                  {canReview && s.needsReview && !s.isAutomatic && s.purpose === 'None' && s.status !== 'Accepted' && (
                    <Button variant="ghost" size="icon" aria-label={`${s.allowsMultipleFiles ? 'Add a file to' : 'Upload'} ${s.typeName} for the candidate`} onClick={() => setUploading({ slot: s })}>
                      <Upload />
                    </Button>
                  )}
                </div>
              </div>
            ))
          )}
        </CardContent>
      </Card>
      {uploading && <UploadDialog slot={uploading.slot} position={uploading.position} candidateId={candidateId} onClose={() => setUploading(null)} onDone={refresh} />}
      {reviewing && <ResubmitDialog slot={reviewing} onClose={() => setReviewing(null)} onDone={(note) => decide.mutate({ id: reviewing.documentId!, decision: 'NeedsResubmission', note })} />}
      {history && <HistoryDialog documentId={history} onClose={() => setHistory(null)} />}
    </>
  )
}

function ResubmitDialog({ slot, onClose, onDone }: { slot: DocumentSlot; onClose: () => void; onDone: (note: string) => void }) {
  const [note, setNote] = useState('')
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Ask for {slot.typeName} again</DialogTitle>
          <DialogDescription>The candidate is emailed a link and sees your note when they sign in.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor="resubmit-note">What needs fixing? <span className="text-destructive">*</span></Label>
          <textarea id="resubmit-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" placeholder="e.g. The photo page is cut off." />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            disabled={!note.trim()}
            onClick={() => {
              onDone(note)
              onClose()
            }}
          >
            Send back
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function HistoryDialog({ documentId, onClose }: { documentId: string; onClose: () => void }) {
  const detail = useQuery({ queryKey: ['documents', 'detail', documentId], queryFn: () => get<DocumentDetail>(`/api/documents/${documentId}`) })
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{detail.data?.typeName ?? 'Document'}: history</DialogTitle>
          <DialogDescription>Every file ever uploaded and every decision. Nothing is overwritten.</DialogDescription>
        </DialogHeader>
        {!detail.data ? (
          <Skeleton className="h-32" />
        ) : (
          <ol className="grid gap-3">
            {[...detail.data.versions].reverse().map((v) => (
              <li key={v.versionNumber} className="rounded-lg border p-3 text-sm">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-ink">
                    Version {v.versionNumber} · {v.fileName}
                    {v.removed && <span className="ml-2 text-xs font-normal text-muted-foreground">(taken out)</span>}
                  </span>
                  <Button variant="ghost" size="icon" aria-label={`Download version ${v.versionNumber}`} onClick={() => saveFile(detail.data!.id, v.fileName, v.versionNumber)}>
                    <Download />
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">
                  Uploaded {formatDateTime(v.uploadedAtUtc)}
                  {v.uploadedBy ? ` by ${v.uploadedBy}` : ''} · {(v.sizeBytes / 1024).toFixed(0)} KB
                </p>
                {detail.data!.reviews
                  .filter((r) => r.versionNumber === v.versionNumber)
                  .map((r) => (
                    <p key={r.reviewedAtUtc} className="mt-1 text-xs text-ink">
                      {r.decision === 'NeedsResubmission' ? 'Sent back' : r.decision === 'Accepted' ? 'Accepted' : 'Review started'} {formatDateTime(r.reviewedAtUtc)}
                      {r.reviewer ? ` by ${r.reviewer}` : ''}
                      {r.note ? `: ${r.note}` : ''}
                    </p>
                  ))}
              </li>
            ))}
          </ol>
        )}
      </DialogContent>
    </Dialog>
  )
}

// ─────────────────────────── An application's checklist ───────────────────────────

export function ChecklistCard({ applicationId, candidateId, finished, visaCaseId, title = 'Required documents' }: { applicationId: string; candidateId: string; finished: boolean; visaCaseId?: string; title?: string }) {
  const roles = useDocumentRoles()
  const { state } = useAuth()
  const canManageChecklist = visaCaseId ? state.status === 'signedIn' && hasAnyRole(state.session, visaRoles) : roles.canManageChecklist
  const base = visaCaseId ? `/api/visa-cases/${visaCaseId}` : `/api/admissions/${applicationId}`
  const queryClient = useQueryClient()
  const checklist = useQuery({ queryKey: ['documents', 'checklist', visaCaseId ?? applicationId], queryFn: () => get<ChecklistResult>(`${base}/checklist`) })
  const types = useQuery({ queryKey: ['document-types'], queryFn: () => get<DocumentTypeDto[]>('/api/document-types'), enabled: canManageChecklist })
  const [waiving, setWaiving] = useState<DocumentSlot | null>(null)
  const [adding, setAdding] = useState('')

  const update = (c: ChecklistResult) => {
    queryClient.setQueryData(['documents', 'checklist', visaCaseId ?? applicationId], c)
    void queryClient.invalidateQueries({ queryKey: [visaCaseId ? 'visa' : 'admissions', visaCaseId ?? applicationId] })
  }
  const refresh = useMutation({
    mutationFn: () => post<ChecklistResult>(`${base}/checklist/refresh`),
    onSuccess: (c) => {
      update(c)
      toast.success('The checklist now matches the current template.')
    },
    onError: (e) => toast.error(message(e)),
  })
  const setItem = useMutation({
    mutationFn: (v: { documentTypeId: string; required: boolean; note?: string }) => put<ChecklistResult>(`${base}/checklist/items`, { ...v, note: v.note?.trim() || null }),
    onSuccess: update,
    onError: (e) => toast.error(message(e)),
  })

  const items = checklist.data?.items ?? []
  // What the student still has to upload: required, and missing or sent back.
  const stillNeeded = items.filter((i) => i.slot.required && (i.slot.status === 'Missing' || i.slot.status === 'NeedsResubmission')).map((i) => i.slot.typeName)
  const addable = (types.data ?? []).filter((t) => t.isActive && !t.isAutomatic && t.purpose === 'None' && !items.some((i) => i.slot.documentTypeId === t.id))

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
        <div>
          <CardTitle className="text-lg text-ink">{title}</CardTitle>
          <CardDescription>
            {checklist.data
              ? visaCaseId
                ? checklist.data.gaps.missing === 0
                  ? 'Everything required has been uploaded. Review happens on the visa platform.'
                  : `${checklist.data.gaps.missing} required still to upload`
                : checklist.data.gaps.notAccepted === 0
                  ? 'Everything required has been accepted.'
                  : `${checklist.data.gaps.notAccepted} required not yet accepted · ${checklist.data.gaps.missing} missing`
              : 'Loading…'}
          </CardDescription>
        </div>
        {canManageChecklist && !finished && (
          <Button variant="outline" size="sm" onClick={() => refresh.mutate()} disabled={refresh.isPending} title="Bring this checklist in line with the current template">
            <RefreshCw /> Refresh from template
          </Button>
        )}
      </CardHeader>
      <CardContent className="grid gap-2">
        {checklist.isError ? (
          <FormError message="We couldn’t load the checklist." />
        ) : !checklist.data ? (
          <Skeleton className="h-24" />
        ) : (
          items.map(({ slot: s, source, note }) => (
            <div key={s.documentTypeId} className={`flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm ${s.required ? '' : 'opacity-70'}`}>
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
                  {s.typeName}
                  {!s.required && <Badge variant="outline">Not required</Badge>}
                  {source === 'Manual' && <Badge variant="secondary">Added by staff</Badge>}
                </p>
                {note && <p className="text-xs text-muted-foreground">{note}</p>}
                {s.allowsMultipleFiles && s.files.length > 0 ? <SlotFileList slot={s} canChange={false} /> : s.fileName && <p className="text-xs text-muted-foreground">{s.fileName}</p>}
              </div>
              <div className="flex items-center gap-1">
                <SlotBadge status={s.status} />
                {s.documentId && s.fileName && !s.allowsMultipleFiles && (
                  <Button variant="ghost" size="icon" aria-label={`Download ${s.typeName}`} onClick={() => saveFile(s.documentId!, s.fileName!)}>
                    <Download />
                  </Button>
                )}
                {canManageChecklist && !finished && !s.isAutomatic && (
                  <Button variant="ghost" size="sm" onClick={() => (s.required ? setWaiving(s) : setItem.mutate({ documentTypeId: s.documentTypeId, required: true }))}>
                    {s.required ? 'Waive' : 'Require'}
                  </Button>
                )}
              </div>
            </div>
          ))
        )}
        {canManageChecklist && !finished && addable.length > 0 && (
          <div className="mt-2 flex items-end gap-2">
            <SelectField label="Ask for another document" className="flex-1" options={addable.map((t) => ({ value: t.id, label: t.name }))} value={adding} onChange={(e) => setAdding(e.target.value)} />
            <Button
              variant="outline"
              disabled={!adding || setItem.isPending}
              onClick={() => {
                setItem.mutate({ documentTypeId: adding, required: true })
                setAdding('')
              }}
            >
              <Plus /> Add
            </Button>
          </div>
        )}
        {canManageChecklist && !finished && stillNeeded.length > 0 && (
          <WhatsAppShareButton
            candidateId={candidateId}
            topic={visaCaseId ? 'visa-documents' : 'documents'}
            intro={visaCaseId ? 'We still need these documents for your visa application:' : 'We still need these documents for your application:'}
            lines={stillNeeded}
            portalPath="/portal/documents"
            label="Ask for these on WhatsApp"
          />
        )}
        <p className="text-xs text-muted-foreground">Upload, review and download these on the candidate’s record.</p>
      </CardContent>
      {waiving && (
        <WaiveDialog
          slot={waiving}
          onClose={() => setWaiving(null)}
          onDone={(note) => setItem.mutate({ documentTypeId: waiving.documentTypeId, required: false, note })}
        />
      )}
    </Card>
  )
}

function WaiveDialog({ slot, onClose, onDone }: { slot: DocumentSlot; onClose: () => void; onDone: (note: string) => void }) {
  const [note, setNote] = useState('')
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Waive {slot.typeName}?</DialogTitle>
          <DialogDescription>This application will no longer require it. Say why, so the history makes sense later.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor="waive-note">Reason <span className="text-destructive">*</span></Label>
          <textarea id="waive-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={1000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button
            disabled={!note.trim()}
            onClick={() => {
              onDone(note)
              onClose()
            }}
          >
            Waive
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
