import { useState } from 'react'
import { Loader2, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { ApiError, postForm } from '@/lib/api'
import { acceptedTypes, maxUploadBytes, slotLabel, type DocumentDetail, type DocumentSlot, type SlotStatus } from '@/lib/documents'

export function SlotBadge({ status, label }: { status: SlotStatus; label?: string }) {
  const variant = status === 'Accepted' ? 'default' : status === 'NeedsResubmission' ? 'destructive' : status === 'Missing' ? 'outline' : 'secondary'
  return <Badge variant={variant}>{label ?? slotLabel[status]}</Badge>
}

/** Choose a file (and an expiry date where the document has one) and upload it as a new version. */
export type UploadTarget = Pick<DocumentSlot, 'documentTypeId' | 'typeName' | 'description' | 'applicationId' | 'applicationLabel' | 'tracksExpiry' | 'version'> & { allowsMultipleFiles?: boolean }

/** For a document that holds several files, pass `position` to replace that file; leave it out to add another. */
export function UploadDialog({ slot, candidateId, position, onClose, onDone }: { slot: UploadTarget; candidateId?: string; position?: number; onClose: () => void; onDone: (d: DocumentDetail) => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [expires, setExpires] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const tooBig = file !== null && file.size > maxUploadBytes
  const submit = async () => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const form = new FormData()
      form.set('DocumentTypeId', slot.documentTypeId)
      if (slot.applicationId) form.set('ApplicationId', slot.applicationId)
      if (candidateId) form.set('CandidateId', candidateId)
      if (position !== undefined) form.set('Position', String(position))
      if (slot.tracksExpiry && expires) form.set('ExpiresOn', expires)
      form.set('File', file)
      const detail = await postForm<DocumentDetail>('/api/documents', form)
      toast.success(`${slot.typeName.charAt(0).toUpperCase()}${slot.typeName.slice(1)} uploaded.`)
      onDone(detail)
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'We couldn’t reach Nichotan. Check your connection and try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{position !== undefined ? `Replace a file: ${slot.typeName}` : slot.allowsMultipleFiles ? `Add a file: ${slot.typeName}` : slot.version > 0 ? `Replace ${slot.typeName}` : `Upload ${slot.typeName}`}</DialogTitle>
          <DialogDescription>
            {slot.description ?? 'Choose a clear scan or photo.'} PDF, JPG or PNG, up to 10 MB.
            {slot.applicationLabel ? ` For ${slot.applicationLabel}.` : ''}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="File" type="file" accept={acceptedTypes} onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="[&_input]:h-auto [&_input]:py-2" error={tooBig ? 'This file is over 10 MB.' : undefined} />
          {slot.tracksExpiry && <Field label="Expiry date (optional)" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />}
          {(position !== undefined || (!slot.allowsMultipleFiles && slot.version > 0)) && <p className="text-xs text-muted-foreground">The earlier file is kept on record. The new one will be reviewed again.</p>}
          {slot.allowsMultipleFiles && position === undefined && <p className="text-xs text-muted-foreground">Each file is added to the others you’ve uploaded here. Add them one at a time, or use “Upload several at once”.</p>}
          <FormError message={error} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} disabled={!file || tooBig || busy}>
            {busy ? <Loader2 className="animate-spin" /> : <Upload />} Upload
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
