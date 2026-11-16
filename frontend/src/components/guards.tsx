import type { ReactNode } from 'react'
import { Link, Navigate, Outlet, useLocation } from 'react-router-dom'
import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import PortalLayout from '@/layouts/PortalLayout'
import StaffLayout from '@/layouts/StaffLayout'
import type { Role } from '@/lib/api'
import { hasAnyRole, homePath, useAuth } from '@/lib/auth'

// These guards decide what to show; the API decides what anyone may actually see or do.

function Loading() {
  return (
    <div className="flex min-h-svh items-center justify-center">
      <Loader2 className="size-8 animate-spin text-muted-foreground" aria-label="Loading" />
    </div>
  )
}

export function RequireSignIn() {
  const { state } = useAuth()
  const location = useLocation()
  if (state.status === 'loading') return <Loading />
  if (state.status === 'signedOut') return <Navigate to={`/login?next=${encodeURIComponent(location.pathname + location.search)}`} replace />
  return <Outlet />
}

/** Pages for people who aren't signed in; signed-in visitors go to their home page. */
export function GuestOnly() {
  const { state } = useAuth()
  if (state.status === 'loading') return <Loading />
  if (state.status === 'signedIn') return <Navigate to={homePath(state.session)} replace />
  return <Outlet />
}

export function RequireStaff({ roles, children }: { roles?: Role[]; children?: ReactNode }) {
  const { state } = useAuth()
  if (state.status !== 'signedIn') return null
  const { session } = state
  if (!session.user.isStaff) return <Navigate to="/portal" replace />
  if (session.mfaSetupRequired) return <Navigate to="/account/security" replace />
  if (roles && !hasAnyRole(session, roles)) return <NoAccess />
  return children ?? <Outlet />
}

export function RequireCandidate() {
  const { state } = useAuth()
  if (state.status !== 'signedIn') return null
  if (state.session.user.isStaff) return <Navigate to="/staff" replace />
  return <Outlet />
}

/** The security page sits inside whichever shell the person normally uses. */
export function AccountShell() {
  const { state } = useAuth()
  if (state.status !== 'signedIn') return null
  return state.session.user.isStaff ? <StaffLayout /> : <PortalLayout />
}

function NoAccess() {
  return (
    <div className="py-16 text-center">
      <h1 className="text-xl font-bold text-ink">You don’t have access to this page</h1>
      <p className="mt-2 text-muted-foreground">Ask an administrator if you need it for your work.</p>
      <Button asChild variant="outline" className="mt-6">
        <Link to="/staff">Back to the dashboard</Link>
      </Button>
    </div>
  )
}

export function NotFound() {
  return (
    <div className="flex min-h-svh flex-col items-center justify-center gap-4 px-4 text-center">
      <h1 className="text-2xl font-bold text-ink">Page not found</h1>
      <p className="text-muted-foreground">The page you’re looking for doesn’t exist or has moved.</p>
      <Button asChild>
        <Link to="/">Go to the home page</Link>
      </Button>
    </div>
  )
}
