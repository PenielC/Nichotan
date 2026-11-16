import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom'
import { FileText, FolderOpen, GraduationCap, Home, LogOut, ShieldCheck, UserRound, type LucideIcon } from 'lucide-react'
import { Crest, Wordmark } from '@/components/brand'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/lib/auth'
import { cn } from '@/lib/utils'

// Documents and Messages join these in later milestones.
const nav: { to: string; label: string; icon: LucideIcon; phone?: boolean }[] = [
  { to: '/portal', label: 'Home', icon: Home },
  { to: '/portal/profile', label: 'My details', icon: UserRound },
  { to: '/portal/application', label: 'Form', icon: FileText },
  { to: '/portal/applications', label: 'Applications', icon: GraduationCap },
  { to: '/portal/documents', label: 'Documents', icon: FolderOpen },
  { to: '/account/security', label: 'Security', icon: ShieldCheck, phone: false },
]

/** The candidate portal: a top bar on larger screens, a bottom tab bar on phones. */
export default function PortalLayout() {
  const { signOut } = useAuth()
  const navigate = useNavigate()
  const out = async () => {
    await signOut()
    navigate('/login?notice=signed-out', { replace: true })
  }

  return (
    <div className="min-h-svh pb-20 sm:pb-0">
      <header className="sticky top-0 z-10 border-b bg-card/90 backdrop-blur">
        <div className="mx-auto flex max-w-5xl items-center gap-6 px-4 py-3 sm:px-6">
          <Link to="/portal" className="flex items-center gap-2.5 text-xs">
            <Crest className="size-10" />
            <Wordmark />
          </Link>
          <nav aria-label="Portal" className="ml-auto hidden items-center gap-1 sm:flex">
            {nav.map(({ to, label }) => (
              <NavLink
                key={to}
                to={to}
                end
                className={({ isActive }) => cn('rounded-lg px-3 py-2 text-sm font-medium', isActive ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:text-foreground')}
              >
                {label}
              </NavLink>
            ))}
          </nav>
          <Button variant="ghost" size="sm" className="ml-auto sm:ml-0" onClick={out}>
            <LogOut />
            Sign out
          </Button>
        </div>
      </header>
      <main className="mx-auto w-full max-w-5xl px-4 py-6 sm:px-6 sm:py-10">
        <Outlet />
      </main>
      <nav aria-label="Portal" className="fixed inset-x-0 bottom-0 z-10 grid grid-cols-5 border-t bg-card sm:hidden">
        {nav.filter((n) => n.phone !== false).map(({ to, label, icon: Icon }) => (
          <NavLink key={to} to={to} end className={({ isActive }) => cn('flex flex-col items-center gap-1 py-2.5 text-xs font-medium', isActive ? 'text-primary' : 'text-muted-foreground')}>
            <Icon className="size-5" aria-hidden />
            {label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
