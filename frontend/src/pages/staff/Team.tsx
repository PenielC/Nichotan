import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Loader2, MoreHorizontal, UserPlus } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { applyServerErrors } from '@/lib/form-errors'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { PageHeader } from '@/components/page-header'
import { ApiError, get, post, put, type Role, type StaffMember } from '@/lib/api'
import { roleLabels, staffRoles, useAuth } from '@/lib/auth'
import { formatDate } from '@/lib/format'
import { personName } from '@/lib/schemas'

const roleHelp: Record<string, string> = {
  Administrator: 'Everything, including staff accounts and the audit log.',
  AdmissionsCounsellor: 'Works with students on admissions, documents, visas and reports.',
}

export default function Team() {
  const { state } = useAuth()
  const [params, setParams] = useSearchParams()
  const [editing, setEditing] = useState<StaffMember | null>(null)
  const staff = useQuery({ queryKey: ['staff'], queryFn: () => get<StaffMember[]>('/api/staff') })
  const inviting = params.get('invite') === '1'
  const myId = state.status === 'signedIn' ? state.session.user.id : null

  return (
    <>
      <PageHeader
        title="Staff accounts"
        description="Invite colleagues and choose what each of them can do."
        actions={
          <Button onClick={() => setParams({ invite: '1' })}>
            <UserPlus /> Invite staff
          </Button>
        }
      />
      <Card>
        <CardContent>
          {staff.isError ? (
            <FormError message="We couldn’t load staff accounts. Please refresh the page." />
          ) : !staff.data ? (
            <Skeleton className="h-64" />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Roles</TableHead>
                  <TableHead className="hidden lg:table-cell">Status</TableHead>
                  <TableHead className="hidden md:table-cell">Last sign-in</TableHead>
                  <TableHead className="w-10">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {staff.data.map((member) => (
                  <TableRow key={member.id} className={member.isDisabled ? 'opacity-60' : undefined}>
                    <TableCell>
                      <p className="font-medium text-ink">
                        {member.firstName} {member.lastName}
                        {member.id === myId && <span className="ml-1.5 text-xs font-normal text-muted-foreground">(you)</span>}
                      </p>
                      <p className="text-xs text-muted-foreground">{member.email}</p>
                    </TableCell>
                    <TableCell className="whitespace-normal">
                      <div className="flex flex-wrap gap-1">
                        {member.roles.map((r) => (
                          <Badge key={r} variant="secondary">
                            {roleLabels[r]}
                          </Badge>
                        ))}
                      </div>
                    </TableCell>
                    <TableCell className="hidden lg:table-cell">
                      <StatusBadge member={member} />
                    </TableCell>
                    <TableCell className="hidden text-muted-foreground md:table-cell">{member.lastSignInAtUtc ? formatDate(member.lastSignInAtUtc) : 'Never'}</TableCell>
                    <TableCell>
                      <MemberActions member={member} isMe={member.id === myId} onEditRoles={() => setEditing(member)} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <InviteDialog open={inviting} onClose={() => setParams({})} />
      <RolesDialog member={editing} onClose={() => setEditing(null)} />
    </>
  )
}

function StatusBadge({ member }: { member: StaffMember }) {
  if (member.isDisabled) return <Badge variant="destructive">Disabled</Badge>
  if (member.invitationPending) return <Badge variant="outline">Invitation sent</Badge>
  if (!member.mfaEnabled) return <Badge variant="outline">No authenticator yet</Badge>
  return <Badge className="bg-accent text-accent-foreground">Active</Badge>
}

function useStaffAction() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ path }: { path: string; done: string }) => post<StaffMember | undefined>(path),
    onSuccess: (_, { done }) => {
      toast.success(done)
      void queryClient.invalidateQueries({ queryKey: ['staff'] })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'That didn’t work. Please try again.'),
  })
}

function MemberActions({ member, isMe, onEditRoles }: { member: StaffMember; isMe: boolean; onEditRoles: () => void }) {
  const action = useStaffAction()
  const [confirmReset, setConfirmReset] = useState(false)
  const run = (path: string, done: string) => action.mutate({ path: `/api/staff/${member.id}/${path}`, done })

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon" aria-label={`Actions for ${member.firstName} ${member.lastName}`} disabled={action.isPending}>
            {action.isPending ? <Loader2 className="animate-spin" /> : <MoreHorizontal />}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onSelect={onEditRoles}>Change roles</DropdownMenuItem>
          {member.invitationPending && !member.isDisabled && <DropdownMenuItem onSelect={() => run('resend-invitation', 'Invitation sent again.')}>Resend invitation</DropdownMenuItem>}
          {member.mfaEnabled && !isMe && <DropdownMenuItem onSelect={() => setConfirmReset(true)}>Reset authenticator</DropdownMenuItem>}
          {!isMe && (
            <>
              <DropdownMenuSeparator />
              {member.isDisabled ? (
                <DropdownMenuItem onSelect={() => run('enable', 'Account enabled.')}>Enable account</DropdownMenuItem>
              ) : (
                <DropdownMenuItem variant="destructive" onSelect={() => run('disable', 'Account disabled. They have been signed out.')}>
                  Disable account
                </DropdownMenuItem>
              )}
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
      <Dialog open={confirmReset} onOpenChange={setConfirmReset}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Reset {member.firstName}’s authenticator?</DialogTitle>
            <DialogDescription>
              Do this only after confirming who is asking, for example by phone. They’ll be signed out and must set up an authenticator app again before using staff features.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setConfirmReset(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={() => {
                setConfirmReset(false)
                run('reset-mfa', 'Authenticator reset.')
              }}
            >
              Reset authenticator
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}

function RoleChecklist({ value, onChange, error }: { value: Role[]; onChange: (roles: Role[]) => void; error?: string }) {
  return (
    <fieldset className="grid gap-2">
      <legend className="mb-1 text-sm font-medium">Roles</legend>
      {staffRoles.map((role) => (
        <Label key={role} className="flex items-start gap-3 rounded-lg border p-3 font-normal has-data-[state=checked]:border-primary has-data-[state=checked]:bg-accent/50">
          <Checkbox checked={value.includes(role)} onCheckedChange={(checked) => onChange(checked ? [...value, role] : value.filter((r) => r !== role))} className="mt-0.5" />
          <span>
            <span className="block text-sm font-medium text-ink">{roleLabels[role]}</span>
            <span className="block text-xs text-muted-foreground">{roleHelp[role]}</span>
          </span>
        </Label>
      ))}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </fieldset>
  )
}

const inviteSchema = z.object({
  firstName: personName('first name', 'their'),
  lastName: personName('last name', 'their'),
  email: z.string().trim().min(1, 'Enter their email address.').email('Enter a valid email address.'),
  roles: z.array(z.string()).min(1, 'Choose at least one role.'),
})
type InviteValues = z.infer<typeof inviteSchema>

function InviteDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<InviteValues>({ resolver: zodResolver(inviteSchema), defaultValues: { firstName: '', lastName: '', email: '', roles: [] } })
  const { errors, isSubmitting } = form.formState
  const roles = useWatch({ control: form.control, name: 'roles' }) as Role[]
  const close = () => {
    form.reset()
    setFormError(null)
    onClose()
  }

  const submit = form.handleSubmit(async (values) => {
    setFormError(null)
    try {
      const member = await post<StaffMember>('/api/staff', values)
      toast.success(`Invitation sent to ${member.email}.`)
      void queryClient.invalidateQueries({ queryKey: ['staff'] })
      close()
    } catch (error) {
      setFormError(applyServerErrors(error, ['firstName', 'lastName', 'email', 'roles'], form.setError))
    }
  })

  return (
    <Dialog open={open} onOpenChange={(o) => !o && close()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Invite a staff member</DialogTitle>
          <DialogDescription>They’ll get an email link, valid for 72 hours, to choose a password and set up an authenticator app.</DialogDescription>
        </DialogHeader>
        <form id="invite" onSubmit={submit} noValidate className="grid gap-4">
          <FormError message={formError} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="First name" error={errors.firstName?.message} {...form.register('firstName')} />
            <Field label="Last name" error={errors.lastName?.message} {...form.register('lastName')} />
          </div>
          <Field label="Work email" type="email" error={errors.email?.message} {...form.register('email')} />
          <RoleChecklist value={roles} onChange={(roles) => form.setValue('roles', roles, { shouldValidate: form.formState.isSubmitted })} error={errors.roles?.message} />
        </form>
        <DialogFooter>
          <Button variant="ghost" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form="invite" disabled={isSubmitting}>
            {isSubmitting && <Loader2 className="animate-spin" />}
            Send invitation
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function RolesDialog({ member, onClose }: { member: StaffMember | null; onClose: () => void }) {
  return (
    <Dialog open={member !== null} onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-lg">{member && <RolesForm key={member.id} member={member} onClose={onClose} />}</DialogContent>
    </Dialog>
  )
}

function RolesForm({ member, onClose }: { member: StaffMember; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [roles, setRoles] = useState<Role[]>(member.roles)
  const save = useMutation({
    mutationFn: () => put<StaffMember>(`/api/staff/${member.id}/roles`, { roles }),
    onSuccess: () => {
      toast.success('Roles updated. The change applies straight away.')
      void queryClient.invalidateQueries({ queryKey: ['staff'] })
      onClose()
    },
  })

  return (
    <>
      <DialogHeader>
        <DialogTitle>
          Roles for {member.firstName} {member.lastName}
        </DialogTitle>
        <DialogDescription>Changes take effect on their very next request.</DialogDescription>
      </DialogHeader>
      <FormError message={save.error ? (save.error instanceof ApiError ? save.error.message : 'That didn’t work. Please try again.') : null} />
      <RoleChecklist value={roles} onChange={setRoles} error={roles.length === 0 ? 'Choose at least one role.' : undefined} />
      <DialogFooter>
        <Button variant="ghost" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={() => save.mutate()} disabled={save.isPending || roles.length === 0}>
          {save.isPending && <Loader2 className="animate-spin" />}
          Save roles
        </Button>
      </DialogFooter>
    </>
  )
}
