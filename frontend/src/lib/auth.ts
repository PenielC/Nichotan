import { createContext, use } from 'react'
import type { Role, Session } from './api'

export type AuthState = { status: 'loading'; session: null } | { status: 'signedOut'; session: null } | { status: 'signedIn'; session: Session }

export interface AuthContextValue {
  state: AuthState
  /** Starts using a session the API just returned (sign-in, registration, MFA step). */
  begin: (session: Session) => void
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const value = use(AuthContext)
  if (!value) throw new Error('useAuth must be used inside AuthProvider')
  return value
}

export const roleLabels: Record<Role, string> = {
  Administrator: 'Administrator',
  AdmissionsCounsellor: 'Counsellor',
  Candidate: 'Student',
}

// Staff are administrators and counsellors, and both do all of this work. The separate names say which kind of work a screen is for.
export const staffRoles: Role[] = ['Administrator', 'AdmissionsCounsellor']

/** Roles that work on individual student records (the server enforces the same list). */
export const caseWorkerRoles: Role[] = staffRoles

/** Roles that manage the directory and admission applications. */
export const admissionsRoles: Role[] = staffRoles

/** Roles that may read reports: counts only (the server enforces the same list). */
export const reportRoles: Role[] = staffRoles

/** Roles that open and progress visa cases. */
export const visaRoles: Role[] = staffRoles

export const hasAnyRole = (session: Session, roles: Role[]) => session.user.roles.some((r) => roles.includes(r))

/** Where a signed-in person lands. Staff without an authenticator are sent to set one up first. */
export function homePath(session: Session): string {
  if (session.user.isStaff) return session.mfaSetupRequired ? '/account/security' : '/staff'
  return '/portal'
}
