import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { BarChart3, Building2, ClipboardList, FileText, FolderCheck, ListChecks, MessageCircleQuestion, GraduationCap, LayoutDashboard, LogOut, Mail, Menu, ScrollText, ShieldCheck, Stamp, UserCog, Users, type LucideIcon } from 'lucide-react'
import { Crest, Wordmark } from '@/components/brand'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetTitle } from '@/components/ui/sheet'
import { caseWorkerRoles, hasAnyRole, reportRoles, roleLabels, useAuth } from '@/lib/auth'
import { get, type Role, type Session } from '@/lib/api'
import type { EnquirySummary } from '@/lib/enquiries'
import { cn } from '@/lib/utils'

interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  roles?: Role[]
}

// Later milestones add Admissions, Visa management, Documents, Messages and Reports here.
const nav: NavItem[] = [
  { to: '/staff', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/staff/tasks', label: 'Tasks', icon: ClipboardList, roles: caseWorkerRoles },
  { to: '/staff/candidates', label: 'Candidates', icon: Users, roles: caseWorkerRoles },
  { to: '/staff/enquiries', label: 'Enquiries', icon: MessageCircleQuestion, roles: caseWorkerRoles },
  { to: '/staff/applications', label: 'Application forms', icon: FileText, roles: caseWorkerRoles },
  { to: '/staff/admissions', label: 'Admissions', icon: GraduationCap, roles: caseWorkerRoles },
  { to: '/staff/visa', label: 'Visa cases', icon: Stamp, roles: caseWorkerRoles },
  { to: '/staff/reports', label: 'Reports', icon: BarChart3, roles: reportRoles },
  { to: '/staff/directory', label: 'Institutions', icon: Building2, roles: caseWorkerRoles },
  { to: '/staff/documents', label: 'Documents', icon: FolderCheck, roles: caseWorkerRoles },
  { to: '/staff/checklists', label: 'Checklists', icon: ListChecks, roles: ['Administrator'] },
  { to: '/staff/team', label: 'Staff accounts', icon: UserCog, roles: ['Administrator'] },
  { to: '/staff/messages', label: 'Messages', icon: Mail, roles: ['Administrator'] },
  { to: '/staff/audit', label: 'Audit log', icon: ScrollText, roles: ['Administrator'] },
  { to: '/account/security', label: 'Account security', icon: ShieldCheck },
]

const initials = (first: string, last: string) => `${first.charAt(0)}${last.charAt(0)}`.toUpperCase()

function Sidebar({ session, onNavigate }: { session: Session; onNavigate?: () => void }) {
  const { signOut } = useAuth()
  const navigate = useNavigate()
  const { user } = session
  // Until the authenticator is set up, only the security page works.
  // How many enquiries nobody has opened yet, shown beside the menu item.
  const enquiries = useQuery({ queryKey: ['enquiries', 'summary'], queryFn: () => get<EnquirySummary>('/api/enquiries/summary'), enabled: hasAnyRole(session, caseWorkerRoles) && !session.mfaSetupRequired, refetchInterval: 60_000 })
  const items = nav.filter((item) => (!item.roles || hasAnyRole(session, item.roles)) && (!session.mfaSetupRequired || item.to === '/account/security'))

  return (
    <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">
      <div className="flex items-center gap-3 px-5 py-6 text-sm">
        <Crest className="size-11" />
        <Wordmark onDark />
      </div>
      <nav aria-label="Staff" className="flex-1 space-y-1 px-3">
        {items.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            end={to === '/staff'}
            onClick={onNavigate}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors',
                isActive ? 'bg-sidebar-primary text-sidebar-primary-foreground' : 'hover:bg-sidebar-accent hover:text-sidebar-accent-foreground',
              )
            }
          >
            <Icon className="size-4" aria-hidden />
            {label}
            {to === '/staff/enquiries' && !!enquiries.data?.unviewed && (
              <span className="ml-auto rounded-full bg-destructive px-2 py-0.5 text-xs font-semibold text-white" aria-label={`${enquiries.data.unviewed} not yet viewed`}>
                {enquiries.data.unviewed}
              </span>
            )}
          </NavLink>
        ))}
      </nav>
      <div className="flex items-center gap-3 border-t border-sidebar-border px-5 py-4">
        <span className="flex size-9 items-center justify-center rounded-full bg-sidebar-accent text-xs font-semibold text-white">{initials(user.firstName, user.lastName)}</span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-white">
            {user.firstName} {user.lastName}
          </p>
          <p className="truncate text-xs">{user.roles.map((r) => roleLabels[r]).join(', ')}</p>
        </div>
        <button
          type="button"
          aria-label="Sign out"
          className="rounded-md p-1.5 hover:bg-sidebar-accent hover:text-white"
          onClick={async () => {
            await signOut()
            navigate('/login?notice=signed-out', { replace: true })
          }}
        >
          <LogOut className="size-4" />
        </button>
      </div>
    </div>
  )
}

export default function StaffLayout() {
  const { state } = useAuth()
  const [open, setOpen] = useState(false)
  if (state.status !== 'signedIn') return null
  const session = state.session

  return (
    <div className="min-h-svh lg:grid lg:grid-cols-[16rem_1fr]">
      <aside className="sticky top-0 hidden h-svh lg:block">
        <Sidebar session={session} />
      </aside>
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent side="left" className="w-64 border-0 p-0" showCloseButton={false}>
          <SheetTitle className="sr-only">Navigation</SheetTitle>
          <Sidebar session={session} onNavigate={() => setOpen(false)} />
        </SheetContent>
      </Sheet>
      <div className="min-w-0">
        <header className="sticky top-0 z-10 flex items-center gap-3 border-b bg-card/90 px-4 py-3 backdrop-blur lg:hidden">
          <Button variant="ghost" size="icon" aria-label="Open navigation" onClick={() => setOpen(true)}>
            <Menu />
          </Button>
          <Wordmark className="text-xs" />
        </header>
        <main className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
