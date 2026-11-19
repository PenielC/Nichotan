import { useState } from 'react'
import { Download, FileText, Loader2, RefreshCw, X } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { ApiError, del, download } from '@/lib/api'
import { formatFileSize, type DocumentDetail, type DocumentSlot } from '@/lib/documents'
import { formatDate } from '@/lib/format'

/**
 * The files in a document that holds several (academic records): each can be downloaded, and when `canChange` is set replaced or
 * taken out. Taking a file out keeps it on record. Nothing here needs the document to have been reviewed.
 */
export function SlotFileList({ slot, canChange, onReplace, onChanged }: { slot: DocumentSlot; canChange: boolean; onReplace?: (position: number) => void; onChanged?: () => void }) {
  const [confirming, setConfirming] = useState<number | null>(null)
  const [busy, setBusy] = useState<number | null>(null)

  const save = async (versionNumber: number, fileName: string) => {
    try {
      await download(`/api/documents/${slot.documentId}/file?version=${versionNumber}`, fileName)
    } catch {
      toast.error('We couldn’t download the file. Please try again.')
    }
  }

  const remove = async (position: number) => {
    setBusy(position)
    try {
      await del<DocumentDetail>(`/api/documents/${slot.documentId}/files/${position}`)
      setConfirming(null)
      onChanged?.()
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : 'We couldn’t remove the file. Please try again.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <ul className="mt-2 grid grid-cols-[minmax(0,1fr)] gap-1.5">
      {slot.files.map((f) => (
        <li key={f.position} className="rounded-md bg-muted/50 px-2.5 py-1.5 text-sm">
          <div className="flex items-center justify-between gap-2">
            <p className="flex min-w-0 items-center gap-2">
              <FileText className="size-4 shrink-0 text-muted-foreground" aria-hidden />
              <span className="min-w-0">
                <span className="block font-medium break-words text-ink [overflow-wrap:anywhere]">{f.fileName}</span>
                <span className="block text-xs text-muted-foreground">
                  {formatFileSize(f.sizeBytes)} · added {formatDate(f.uploadedAtUtc)}
                </span>
              </span>
            </p>
            <div className="flex shrink-0 items-center">
              <Button variant="ghost" size="icon" aria-label={`Download ${f.fileName}`} onClick={() => save(f.versionNumber, f.fileName)}>
                <Download />
              </Button>
              {canChange && onReplace && (
                <Button variant="ghost" size="icon" aria-label={`Replace ${f.fileName}`} onClick={() => onReplace(f.position)}>
                  <RefreshCw />
                </Button>
              )}
              {canChange && (
                <Button variant="ghost" size="icon" aria-label={`Remove ${f.fileName}`} onClick={() => setConfirming(f.position)} disabled={busy !== null}>
                  <X />
                </Button>
              )}
            </div>
          </div>
          {confirming === f.position && (
            <div className="mt-1.5 flex flex-wrap items-center gap-2 text-xs">
              <span className="text-ink">Take this file out?</span>
              <Button size="sm" variant="destructive" onClick={() => remove(f.position)} disabled={busy !== null}>
                {busy === f.position && <Loader2 className="animate-spin" />} Yes, remove it
              </Button>
              <Button size="sm" variant="outline" onClick={() => setConfirming(null)} disabled={busy !== null}>
                Keep it
              </Button>
            </div>
          )}
        </li>
      ))}
    </ul>
  )
}
