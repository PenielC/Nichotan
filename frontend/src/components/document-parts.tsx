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
export type UploadTarget = Pick<DocumentSlot, 'documentTypeId' | 'typeName' | 'description' | 'applicationId' | 'applicationLabel' | 'tracksExpiry' | 'version'>

export function UploadDialog({ slot, candidateId, onClose, onDone }: { slot: UploadTarget; candidateId?: string; onClose: () => void; onDone: (d: DocumentDetail) => void }) {
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
          <DialogTitle>{slot.version > 0 ? `Replace ${slot.typeName}` : `Upload ${slot.typeName}`}</DialogTitle>
          <DialogDescription>
            {slot.description ?? 'Choose a clear scan or photo.'} PDF, JPG or PNG, up to 10 MB.
            {slot.applicationLabel ? ` For ${slot.applicationLabel}.` : ''}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="File" type="file" accept={acceptedTypes} onChange={(e) => setFile(e.target.files?.[0] ?? null)} className="[&_input]:h-auto [&_input]:py-2" error={tooBig ? 'This file is over 10 MB.' : undefined} />
          {slot.tracksExpiry && <Field label="Expiry date (optional)" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} />}
          {slot.version > 0 && <p className="text-xs text-muted-foreground">The earlier file is kept on record. The new one will be reviewed again.</p>}
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
