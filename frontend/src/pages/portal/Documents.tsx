import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, FileCheck2, Files, Plus, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { BulkUploadDialog } from '@/components/bulk-upload-dialog'
import { SlotFileList } from '@/components/slot-files'
import { SlotBadge, UploadDialog } from '@/components/document-parts'
import { FormError } from '@/components/form'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { download, get } from '@/lib/api'
import { dayText } from '@/lib/admissions'
import { candidateSlotLabel, canUploadTo, maxFilesPerDocument, type DocumentSlot, type DocumentsOverview } from '@/lib/documents'
import { formatDate } from '@/lib/format'

const documentsKey = ['documents', 'mine'] as const

export default function Documents() {
  const overview = useQuery({ queryKey: documentsKey, queryFn: () => get<DocumentsOverview>('/api/documents/mine') })
  const queryClient = useQueryClient()
  const [uploading, setUploading] = useState<{ slot: DocumentSlot; position?: number } | null>(null)
  const [uploadingMany, setUploadingMany] = useState(false)
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['documents'] })
    void queryClient.invalidateQueries({ queryKey: ['admissions'] })
  }

  const shared = overview.data?.slots.filter((s) => s.applicationId === null) ?? []
  const perApplication = new Map<string, DocumentSlot[]>()
  for (const s of overview.data?.slots ?? []) {
    if (s.applicationId) perApplication.set(s.applicationLabel ?? 'Application', [...(perApplication.get(s.applicationLabel ?? 'Application') ?? []), s])
  }
  const outstanding = overview.data?.slots.filter((s) => s.required && (s.status === 'Missing' || s.status === 'NeedsResubmission') && !s.isAutomatic).length ?? 0

  return (
    <div className="grid grid-cols-[minmax(0,1fr)] gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-2xl font-bold text-ink">My documents</h1>
          <p className="mt-1 text-muted-foreground">Upload what your counsellor asks for. Each file is checked by our team, and you’ll see here if anything needs to be sent again.</p>
        </div>
        {overview.data?.slots.some(canUploadTo) && (
          <Button onClick={() => setUploadingMany(true)}>
            <Files /> Upload several at once
          </Button>
        )}
      </div>

      {overview.isError ? (
        <FormError message="We couldn’t load your documents. Please refresh the page." />
      ) : !overview.data ? (
        <Skeleton className="h-96" />
      ) : (
        <>
          {outstanding > 0 && (
            <Alert>
              <FileCheck2 />
              <AlertTitle>
                {outstanding} document{outstanding === 1 ? '' : 's'} to upload
              </AlertTitle>
              <AlertDescription>Files must be PDF, JPG or PNG, up to 10 MB each. A clear photo from your phone is fine.</AlertDescription>
            </Alert>
          )}
          <Card>
            <CardHeader>
              <CardTitle className="text-lg text-ink">For all your applications</CardTitle>
              <CardDescription>Upload these once. We use them for every school you apply to.</CardDescription>
            </CardHeader>
            <CardContent className="grid grid-cols-[minmax(0,1fr)] gap-3">
              {shared.map((s) => (
                <SlotRow key={s.documentTypeId} slot={s} onUpload={(position) => setUploading({ slot: s, position })} onChanged={refresh} />
              ))}
            </CardContent>
          </Card>
          {[...perApplication.entries()].map(([label, slots]) => (
            <Card key={label}>
              <CardHeader>
                <CardTitle className="text-lg text-ink">{label}</CardTitle>
                <CardDescription>Needed only for this application.</CardDescription>
              </CardHeader>
              <CardContent className="grid grid-cols-[minmax(0,1fr)] gap-3">
                {slots.map((s) => (
                  <SlotRow key={`${s.documentTypeId}-${s.applicationId}`} slot={s} onUpload={(position) => setUploading({ slot: s, position })} onChanged={refresh} />
                ))}
              </CardContent>
            </Card>
          ))}
        </>
      )}

      {uploading && (
        <UploadDialog
          slot={uploading.slot}
          position={uploading.position}
          onClose={() => setUploading(null)}
          onDone={refresh}
        />
      )}
      {uploadingMany && overview.data && <BulkUploadDialog slots={overview.data.slots} onClose={() => setUploadingMany(false)} onDone={refresh} />}
    </div>
  )
}

function SlotRow({ slot, onUpload, onChanged }: { slot: DocumentSlot; onUpload: (position?: number) => void; onChanged: () => void }) {
  // The school's letters are handled on the Applications page.
  const fromSchool = slot.purpose !== 'None'
  const locked = slot.status === 'Accepted' || fromSchool
  const several = slot.allowsMultipleFiles
  const fileCount = slot.files?.length ?? 0
  const save = async () => {
    try {
      await download(`/api/documents/${slot.documentId}/file`, slot.fileName ?? 'document')
    } catch {
      toast.error('We couldn’t download the file. Please try again.')
    }
  }

  return (
    <div className={`rounded-lg border p-3 sm:p-4 ${slot.status === 'NeedsResubmission' ? 'border-destructive/40 bg-destructive/5' : ''}`}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-medium text-ink">
            {slot.typeName}
            <Badge variant="outline">{slot.required ? 'Required' : 'Optional'}</Badge>
          </p>
          {slot.description && <p className="mt-0.5 text-sm text-muted-foreground">{slot.description}</p>}
          {several && fileCount > 0 && <SlotFileList slot={slot} canChange={!locked} onReplace={onUpload} onChanged={onChanged} />}
          {!several && slot.fileName && (
            <p className="mt-1 text-xs text-muted-foreground">
              {slot.fileName} · uploaded {slot.uploadedAtUtc ? formatDate(slot.uploadedAtUtc) : ''}
              {slot.version > 1 ? ` · version ${slot.version}` : ''}
              {slot.expiresOn ? ` · expires ${dayText(slot.expiresOn)}` : ''}
            </p>
          )}
          {slot.reviewNote && (
            <p className="mt-2 rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive">
              <span className="font-medium">What to fix:</span> {slot.reviewNote}
            </p>
          )}
        </div>
        <div className="flex flex-col items-start gap-2 sm:items-end">
          {fromSchool ? <Badge variant="secondary">From your school</Badge> : <SlotBadge status={slot.status} label={slot.isAutomatic ? (slot.status === 'Accepted' ? 'Done' : 'Waiting for your signature') : !slot.needsReview && slot.status === 'Uploaded' ? 'Uploaded' : candidateSlotLabel[slot.status]} />}
          {slot.isAutomatic ? (
            slot.status !== 'Accepted' && (
              <Button asChild variant="outline" size="sm">
                <Link to="/portal/application">Open my form</Link>
              </Button>
            )
          ) : (
            <div className="flex gap-2">
              {slot.documentId && !several && (
                <Button variant="ghost" size="sm" onClick={save} aria-label={`Download ${slot.typeName}`}>
                  <Download />
                </Button>
              )}
              {locked ? (
                <span className="self-center text-xs text-muted-foreground">{fromSchool ? 'Shared with you' : 'Accepted'}</span>
              ) : several && fileCount >= maxFilesPerDocument ? (
                <span className="self-center text-xs text-muted-foreground">{maxFilesPerDocument} files, the most you can add</span>
              ) : (
                <Button size="sm" variant={slot.status === 'Missing' || slot.status === 'NeedsResubmission' ? 'default' : 'outline'} onClick={() => onUpload()}>
                  {several ? <Plus /> : <Upload />} {several ? (fileCount > 0 ? 'Add another file' : 'Add a file') : slot.version > 0 ? 'Replace' : 'Upload'}
                </Button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
