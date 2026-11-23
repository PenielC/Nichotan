import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { PageHeader } from '@/components/page-header'
import { FormError } from '@/components/form'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { get, query } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { cn } from '@/lib/utils'

type MessageState = 'Sent' | 'Failed'
interface MessageLogEntry {
  id: string
  channel: 'Email' | 'WhatsApp'
  subject: string
  recipient: string
  state: MessageState
  error: string | null
  attemptedAtUtc: string
  /** Which of the organisation's addresses sent it. The address itself is configuration and is not stored. */
  sender: 'Automated' | 'Info'
}

const senderLabel: Record<MessageLogEntry['sender'], string> = {
  Automated: 'automated address',
  Info: 'info address',
}
interface MessageLogResult {
  items: MessageLogEntry[]
  sent: number
  failed: number
}

const filters: { value: MessageState | ''; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Sent', label: 'Sent' },
]

/** The record of every email the system tried to send. Recipients are masked and no message text is kept. */
export default function Messages() {
  const [state, setState] = useState<MessageState | ''>('')
  const log = useQuery({ queryKey: ['messages', state], queryFn: () => get<MessageLogResult>(`/api/messages${query({ state: state || undefined })}`) })

  return (
    <div className="grid gap-6">
      <PageHeader title="Messages" description="Every email the system has tried to send, and whether it went. Addresses are partly hidden and the message text isn’t kept." />

      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Show">
        {filters.map((f) => (
          <button
            key={f.label}
            type="button"
            onClick={() => setState(f.value)}
            aria-pressed={state === f.value}
            className={cn('rounded-full border px-3 py-1 text-sm', state === f.value ? 'border-primary bg-accent text-primary' : 'text-muted-foreground hover:bg-muted')}
          >
            {f.label}
          </button>
        ))}
        {log.data && (
          <span className="ml-auto text-sm text-muted-foreground">
            {log.data.sent} sent · {log.data.failed} failed
          </span>
        )}
      </div>

      {log.isError ? (
        <FormError message="We couldn’t load the messages. Please refresh the page." />
      ) : !log.data ? (
        <Skeleton className="h-48" />
      ) : log.data.items.length === 0 ? (
        <Card>
          <CardContent className="py-8 text-center text-sm text-muted-foreground">No messages yet.</CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="grid gap-0 divide-y p-0">
            {log.data.items.map((m) => (
              <div key={m.id} className="flex flex-wrap items-start justify-between gap-2 px-4 py-3">
                <div className="min-w-0">
                  <p className="truncate font-medium text-ink">{m.subject}</p>
                  <p className="text-sm text-muted-foreground">
                    To {m.recipient} · from the {senderLabel[m.sender] ?? 'automated address'} · {formatDateTime(m.attemptedAtUtc)}
                  </p>
                  {m.error && <p className="mt-1 text-sm text-destructive">{m.error}</p>}
                </div>
                <Badge variant={m.state === 'Failed' ? 'destructive' : 'secondary'}>{m.state}</Badge>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  )
}
