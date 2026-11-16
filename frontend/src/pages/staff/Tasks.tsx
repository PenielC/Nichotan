import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Circle, CircleX, Pencil, Plus, Undo2 } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, get, post, put, query, type Paged } from '@/lib/api'
import { dayText, todayIso } from '@/lib/admissions'
import { useAuth } from '@/lib/auth'
import type { Assignee, StaffTask, TaskPriority, TaskStatus } from '@/lib/tasks'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const priorities: { value: TaskPriority; label: string }[] = [
  { value: 'High', label: 'High' },
  { value: 'Normal', label: 'Normal' },
  { value: 'Low', label: 'Low' },
]
const textArea = 'w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50'

/** What a task is linked to, as a link back to it. */
function TaskLinks({ task }: { task: StaffTask }) {
  return (
    <>
      {task.candidateId && task.candidateName && (
        <Link to={`/staff/candidates/${task.candidateId}`} className="text-primary underline-offset-2 hover:underline">
          {task.candidateName}
        </Link>
      )}
      {task.visaCaseId && (
        <Link to={`/staff/visa/${task.visaCaseId}`} className="text-primary underline-offset-2 hover:underline">
          Visa case
        </Link>
      )}
      {task.applicationId && (
        <Link to={`/staff/admissions/${task.applicationId}`} className="text-primary underline-offset-2 hover:underline">
          Application
        </Link>
      )}
    </>
  )
}

/** One task: tick it off, reopen it, edit it. Overdue and high-priority ones stand out. */
function TaskRow({ task, onEdit, onChanged }: { task: StaffTask; onEdit: (t: StaffTask) => void; onChanged: () => void }) {
  const change = useMutation({
    mutationFn: (action: 'complete' | 'cancel' | 'reopen') => post<StaffTask>(`/api/tasks/${task.id}/status`, { action }),
    onSuccess: onChanged,
    onError: (e) => toast.error(message(e)),
  })
  const open = task.status === 'Open'
  return (
    <li className={`grid gap-1 rounded-lg border p-3 ${task.overdue ? 'border-destructive/40 bg-destructive/5' : ''} ${open ? '' : 'opacity-70'}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="flex min-w-0 items-start gap-2">
          {open ? (
            <button type="button" aria-label={`Mark done: ${task.title}`} className="-m-1.5 flex size-8 shrink-0 items-center justify-center rounded-full text-muted-foreground hover:text-primary" disabled={change.isPending} onClick={() => change.mutate('complete')}>
              <Circle className="size-5" />
            </button>
          ) : (
            <Check className="mt-0.5 size-5 text-primary" aria-hidden />
          )}
          <div className="min-w-0">
            <p className={`font-medium text-ink ${open ? '' : 'line-through'}`}>{task.title}</p>
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
              <span className={task.overdue ? 'font-semibold text-destructive' : undefined}>{task.dueOn ? `${task.overdue ? 'Overdue · ' : 'Due '}${dayText(task.dueOn)}` : 'No due day'}</span>
              <span>{task.assigneeName}</span>
              <TaskLinks task={task} />
            </p>
          </div>
        </div>
        <div className="flex items-center gap-1">
          {task.priority === 'High' && open && <Badge variant="destructive">High</Badge>}
          {task.priority === 'Low' && open && <Badge variant="outline">Low</Badge>}
          {task.status === 'Cancelled' && <Badge variant="outline">Cancelled</Badge>}
          {open ? (
            <>
              <Button variant="ghost" size="icon" aria-label={`Edit ${task.title}`} onClick={() => onEdit(task)}>
                <Pencil />
              </Button>
              <Button variant="ghost" size="icon" aria-label={`Cancel ${task.title}`} disabled={change.isPending} onClick={() => change.mutate('cancel')}>
                <CircleX />
              </Button>
            </>
          ) : (
            <Button variant="ghost" size="sm" disabled={change.isPending} onClick={() => change.mutate('reopen')}>
              <Undo2 /> Reopen
            </Button>
          )}
        </div>
      </div>
      {task.notes && <p className="ml-7 text-sm whitespace-pre-wrap text-ink">{task.notes}</p>}
      {!open && task.completedAtUtc && <p className="ml-7 text-xs text-muted-foreground">{task.status === 'Done' ? 'Done' : 'Cancelled'} {task.completedBy ? `by ${task.completedBy}` : ''}</p>}
    </li>
  )
}

function useAssignees() {
  return useQuery({ queryKey: ['tasks', 'assignees'], queryFn: () => get<Assignee[]>('/api/tasks/assignees') })
}

// ─────────────────────────── The Tasks page ───────────────────────────

export default function Tasks() {
  const queryClient = useQueryClient()
  const [scope, setScope] = useState<'mine' | 'all'>('mine')
  const [status, setStatus] = useState<TaskStatus>('Open')
  const [editing, setEditing] = useState<StaffTask | 'new' | null>(null)
  const list = useQuery({
    queryKey: ['tasks', 'list', scope, status],
    queryFn: () => get<Paged<StaffTask>>(`/api/tasks${query({ mine: scope === 'mine' ? 'true' : '', status, pageSize: 100 })}`),
    staleTime: 0,
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['tasks'] })

  return (
    <>
      <PageHeader
        title="Tasks"
        description="Things for the team to do. Overdue ones come first. Anyone on the staff can finish or reassign a task."
        actions={
          <Button onClick={() => setEditing('new')}>
            <Plus /> Add a task
          </Button>
        }
      />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <SelectField label="Whose" className="w-44" options={[{ value: 'mine', label: 'Mine' }, { value: 'all', label: 'Everyone’s' }]} value={scope} onChange={(e) => setScope(e.target.value as 'mine' | 'all')} />
            <SelectField label="Show" className="w-40" options={[{ value: 'Open', label: 'Open' }, { value: 'Done', label: 'Done' }, { value: 'Cancelled', label: 'Cancelled' }]} value={status} onChange={(e) => setStatus(e.target.value as TaskStatus)} />
          </div>
          {list.isError ? (
            <FormError message="We couldn’t load tasks. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-48" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{status === 'Open' ? 'Nothing to do here. Add a task to get started.' : 'Nothing here.'}</p>
          ) : (
            <ul className="grid gap-2">
              {list.data.items.map((t) => (
                <TaskRow key={t.id} task={t} onEdit={setEditing} onChanged={refresh} />
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
      {editing && <TaskDialog target={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
    </>
  )
}

// ─────────────────────────── Tasks on a student's record, or on a case ───────────────────────────

/** Open tasks tied to one student (and, if given, one application or visa case), with a button to add another already linked. */
export function TasksCard({ candidateId, applicationId, visaCaseId, title = 'Tasks' }: { candidateId?: string; applicationId?: string; visaCaseId?: string; title?: string }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<StaffTask | 'new' | null>(null)
  const params = { candidateId, applicationId, visaCaseId, status: 'Open', pageSize: 50 }
  const list = useQuery({ queryKey: ['tasks', 'for', candidateId, applicationId, visaCaseId], queryFn: () => get<Paged<StaffTask>>(`/api/tasks${query(params)}`), staleTime: 0 })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['tasks'] })
  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-start justify-between gap-2">
        <div>
          <CardTitle className="text-lg text-ink">{title}</CardTitle>
          <CardDescription>Things the team still needs to do here.</CardDescription>
        </div>
        <Button variant="outline" size="sm" onClick={() => setEditing('new')}>
          <Plus /> Add a task
        </Button>
      </CardHeader>
      <CardContent>
        {!list.data ? (
          <Skeleton className="h-16" />
        ) : list.data.items.length === 0 ? (
          <p className="py-3 text-sm text-muted-foreground">No open tasks.</p>
        ) : (
          <ul className="grid gap-2">
            {list.data.items.map((t) => (
              <TaskRow key={t.id} task={t} onEdit={setEditing} onChanged={refresh} />
            ))}
          </ul>
        )}
      </CardContent>
      {editing && <TaskDialog target={editing} link={{ candidateId, applicationId, visaCaseId }} onClose={() => setEditing(null)} onSaved={refresh} />}
    </Card>
  )
}

// ─────────────────────────── Add or edit ───────────────────────────

export function TaskDialog({ target, link, onClose, onSaved }: { target: StaffTask | 'new'; link?: { candidateId?: string; applicationId?: string; visaCaseId?: string }; onClose: () => void; onSaved: () => void }) {
  const existing = target === 'new' ? null : target
  const { state } = useAuth()
  const myId = state.status === 'signedIn' ? state.session.user.id : ''
  const assignees = useAssignees()
  const [title, setTitle] = useState(existing?.title ?? '')
  const [notes, setNotes] = useState(existing?.notes ?? '')
  const [priority, setPriority] = useState<TaskPriority>(existing?.priority ?? 'Normal')
  const [dueOn, setDueOn] = useState(existing?.dueOn ?? '')
  const [assignee, setAssignee] = useState(existing?.assigneeUserId ?? myId)
  const save = useMutation({
    mutationFn: () => {
      const body = { title: title.trim(), notes: notes.trim() || null, priority, dueOn: dueOn || null, assigneeUserId: assignee || null }
      return existing ? put<StaffTask>(`/api/tasks/${existing.id}`, body) : post<StaffTask>('/api/tasks', { ...body, ...link })
    },
    onSuccess: () => {
      toast.success(existing ? 'Task saved.' : 'Task added.')
      onSaved()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{existing ? 'Edit task' : 'Add a task'}</DialogTitle>
          <DialogDescription>{link && (link.candidateId || link.applicationId || link.visaCaseId) ? 'It will be linked to this record.' : 'Say what needs doing, who does it, and by when.'}</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="What needs doing" required value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} />
          <div className="grid gap-1.5">
            <Label htmlFor="task-notes">Notes</Label>
            <textarea id="task-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} maxLength={2000} className={textArea} />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Who does it" options={(assignees.data ?? []).map((a) => ({ value: a.id, label: a.id === myId ? `${a.name} (me)` : a.name }))} value={assignee} onChange={(e) => setAssignee(e.target.value)} />
            <SelectField label="Priority" options={priorities} value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} />
          </div>
          <Field label="Due day (optional)" type="date" min={existing?.dueOn && existing.dueOn < todayIso() ? existing.dueOn : todayIso()} value={dueOn} onChange={(e) => setDueOn(e.target.value)} />
          <FormError message={save.error ? message(save.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button onClick={() => save.mutate()} disabled={!title.trim() || save.isPending}>
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
