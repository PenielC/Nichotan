import { useRef, useState } from 'react'
import { CheckCircle2, FilePlus2, Loader2, Upload, X } from 'lucide-react'
import { toast } from 'sonner'
import { FormError } from '@/components/form'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { ApiError, postForm } from '@/lib/api'
import { acceptedTypes, canUploadTo, formatFileSize, guessSlot, maxFilesPerDocument, maxUploadBytes, slotKey, type DocumentDetail, type DocumentSlot } from '@/lib/documents'

interface Row {
  id: number
  file: File
  slot: string
  expires: string
  state: 'ready' | 'uploading' | 'done' | 'failed'
  error?: string
}

/**
 * Choose several files at once, say which document each one is (the name is used to suggest it), and upload them together.
 * Each file goes up on its own, so one that fails doesn't hold back the others and can be fixed and sent again.
 */
export function BulkUploadDialog({ slots, candidateId, onClose, onDone }: { slots: DocumentSlot[]; candidateId?: string; onClose: () => void; onDone: (d: DocumentDetail) => void }) {
  const input = useRef<HTMLInputElement>(null)
  const nextId = useRef(1)
  const [rows, setRows] = useState<Row[]>([])
  const [busy, setBusy] = useState(false)

  const targets = slots.filter(canUploadTo)
  const byKey = new Map(targets.map((s) => [slotKey(s), s]))
  const label = (s: DocumentSlot) => `${s.typeName.charAt(0).toUpperCase()}${s.typeName.slice(1)}${s.applicationLabel ? ` · ${s.applicationLabel}` : ''}`

  const add = (files: FileList | null) => {
    if (!files?.length) return
    setRows((current) => {
      const taken = new Set(current.map((r) => r.slot).filter(Boolean))
      const added: Row[] = []
      for (const file of Array.from(files)) {
        const slot = guessSlot(file.name, targets, taken) ?? ''
        // A document that holds several files (academic records) can take every file that matches it.
        if (slot && !byKey.get(slot)?.allowsMultipleFiles) taken.add(slot)
        added.push({ id: nextId.current++, file, slot, expires: '', state: 'ready' })
      }
      return [...current, ...added]
    })
    if (input.current) input.current.value = ''
  }

  const change = (id: number, patch: Partial<Row>) => setRows((current) => current.map((r) => (r.id === id ? { ...r, ...patch } : r)))

  // What is wrong with a row before anything is sent.
  const problem = (r: Row): string | null => {
    if (r.state === 'done') return null
    if (r.file.size > maxUploadBytes) return 'This file is over 10 MB.'
    if (!r.slot) return 'Choose which document this is.'
    const target = byKey.get(r.slot)
    const others = rows.filter((o) => o.id !== r.id && o.state !== 'done' && o.slot === r.slot).length
    if (target?.allowsMultipleFiles) {
      if ((target.files?.length ?? 0) + others + 1 > maxFilesPerDocument) return `A document can hold ${maxFilesPerDocument} files, and this one would go over. Remove a file or two.`
      return null
    }
    if (others > 0) return 'Another file is already set for this document. Pick a different one.'
    return null
  }
  const pending = rows.filter((r) => r.state !== 'done')
  const canSend = pending.length > 0 && pending.every((r) => problem(r) === null) && !busy

  const send = async () => {
    setBusy(true)
    let ok = 0
    let failed = 0
    let last: DocumentDetail | null = null
    for (const r of pending) {
      change(r.id, { state: 'uploading', error: undefined })
      try {
        const target = byKey.get(r.slot)!
        const form = new FormData()
        form.set('DocumentTypeId', target.documentTypeId)
        if (target.applicationId) form.set('ApplicationId', target.applicationId)
        if (candidateId) form.set('CandidateId', candidateId)
        if (target.tracksExpiry && r.expires) form.set('ExpiresOn', r.expires)
        form.set('File', r.file)
        last = await postForm<DocumentDetail>('/api/documents', form)
        change(r.id, { state: 'done' })
        ok++
      } catch (e) {
        change(r.id, { state: 'failed', error: e instanceof ApiError ? e.message : 'We couldn’t reach Nichotan. Check your connection and try again.' })
        failed++
      }
    }
    setBusy(false)
    if (last) onDone(last)
    if (failed === 0) {
      toast.success(ok === 1 ? '1 document uploaded.' : `${ok} documents uploaded.`)
      onClose()
    } else {
      toast.error(`${ok} uploaded, ${failed} didn’t go through. Fix the ones marked below and send them again.`)
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Upload several documents</DialogTitle>
          <DialogDescription>Choose all your files at once, then say which document each one is. Several files can go to the same document, such as your academic records. PDF, JPG or PNG, up to 10 MB each.</DialogDescription>
        </DialogHeader>

        <div className="grid gap-3">
          <input ref={input} type="file" multiple accept={acceptedTypes} className="sr-only" aria-label="Choose files" onChange={(e) => add(e.target.files)} />
          <Button type="button" variant={rows.length ? 'outline' : 'default'} className="justify-self-start" onClick={() => input.current?.click()} disabled={busy}>
            <FilePlus2 /> {rows.length ? 'Add more files' : 'Choose files'}
          </Button>

          {rows.length === 0 && <p className="text-sm text-muted-foreground">Tip: on a phone you can pick several photos at once from your gallery.</p>}

          <ul className="grid gap-3">
            {rows.map((r) => {
              const issue = r.state === 'ready' || r.state === 'failed' ? problem(r) : null
              const target = byKey.get(r.slot)
              return (
                <li key={r.id} className={`grid gap-2 rounded-lg border p-3 ${r.state === 'failed' ? 'border-destructive/50 bg-destructive/5' : r.state === 'done' ? 'bg-muted/40' : ''}`}>
                  <div className="flex items-start justify-between gap-2">
                    <p className="min-w-0 text-sm">
                      <span className="block truncate font-medium text-ink">{r.file.name}</span>
                      <span className="text-xs text-muted-foreground">{formatFileSize(r.file.size)}</span>
                    </p>
                    {r.state === 'done' ? (
                      <span className="flex items-center gap-1 text-xs font-medium text-primary">
                        <CheckCircle2 className="size-4" aria-hidden /> Uploaded
                      </span>
                    ) : r.state === 'uploading' ? (
                      <Loader2 className="size-4 animate-spin text-muted-foreground" aria-label="Uploading" />
                    ) : (
                      <button type="button" aria-label={`Remove ${r.file.name}`} className="-m-1 flex size-8 items-center justify-center rounded-md text-muted-foreground hover:bg-muted" disabled={busy} onClick={() => setRows((c) => c.filter((x) => x.id !== r.id))}>
                        <X className="size-4" />
                      </button>
                    )}
                  </div>
                  {r.state !== 'done' && (
                    <>
                      <select
                        aria-label={`Which document is ${r.file.name}?`}
                        value={r.slot}
                        disabled={busy}
                        onChange={(e) => change(r.id, { slot: e.target.value, state: 'ready', error: undefined })}
                        className="h-10 w-full rounded-lg border border-input bg-card px-2.5 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
                      >
                        <option value="">Choose the document…</option>
                        {targets.map((s) => (
                          <option key={slotKey(s)} value={slotKey(s)}>
                            {label(s)}
                            {s.allowsMultipleFiles ? (s.files?.length ? ` (adds to your ${s.files.length} file${s.files.length === 1 ? '' : 's'})` : ' (you can add several)') : s.version > 0 ? ' (replaces your current file)' : ''}
                          </option>
                        ))}
                      </select>
                      {target?.tracksExpiry && (
                        <label className="grid gap-1 text-xs text-muted-foreground">
                          Expiry date (optional)
                          <input type="date" value={r.expires} disabled={busy} onChange={(e) => change(r.id, { expires: e.target.value })} className="h-10 rounded-lg border border-input bg-card px-2.5 text-sm text-foreground" />
                        </label>
                      )}
                      {(issue ?? r.error) && <p className="text-xs text-destructive">{r.error ?? issue}</p>}
                    </>
                  )}
                </li>
              )
            })}
          </ul>
          {targets.length === 0 && <FormError message="There’s nothing you can upload right now." />}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            {rows.some((r) => r.state === 'done') ? 'Close' : 'Cancel'}
          </Button>
          <Button onClick={send} disabled={!canSend}>
            {busy ? <Loader2 className="animate-spin" /> : <Upload />} {pending.length > 1 ? `Upload ${pending.length} files` : 'Upload'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
