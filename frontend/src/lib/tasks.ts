export type TaskPriority = 'Low' | 'Normal' | 'High'
export type TaskStatus = 'Open' | 'Done' | 'Cancelled'

export const priorityLabel: Record<TaskPriority, string> = { Low: 'Low', Normal: 'Normal', High: 'High' }

export interface StaffTask {
  id: string
  title: string
  notes: string | null
  priority: TaskPriority
  status: TaskStatus
  dueOn: string | null
  overdue: boolean
  assigneeUserId: string
  assigneeName: string
  candidateId: string | null
  candidateName: string | null
  applicationId: string | null
  visaCaseId: string | null
  createdBy: string | null
  createdAtUtc: string
  completedAtUtc: string | null
  completedBy: string | null
}

export interface TaskSummary {
  mineOpen: number
  mineOverdue: number
  allOverdue: number
}

export interface Assignee {
  id: string
  name: string
}

/** One line of a student's history. atUtc orders the list; occurredOn is the day staff dated a move to, when it differs. */
export interface TimelineEntry {
  atUtc: string
  occurredOn: string | null
  kind: 'Account' | 'Form' | 'Admission' | 'Visa' | 'Document' | 'Task'
  title: string
  detail: string | null
  by: string | null
  linkType: string | null
  linkId: string | null
}
