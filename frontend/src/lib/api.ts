// The API client. The access token lives only in memory (never localStorage); the refresh token is an
// HttpOnly cookie scoped to /api/auth that this code can't read. On a 401 the client refreshes once and retries.

export type Role = 'Administrator' | 'AdmissionsCounsellor' | 'Candidate'

export interface SessionUser {
  id: string
  email: string
  firstName: string
  lastName: string
  roles: Role[]
  isStaff: boolean
  emailVerified: boolean
  mfaEnabled: boolean
}

export interface Session {
  accessToken: string
  accessTokenExpiresAtUtc: string
  user: SessionUser
  mfaSatisfied: boolean
  mfaSetupRequired: boolean
}

export interface LoginResponse {
  mfaRequired: boolean
  mfaChallengeToken: string | null
  session: Session | null
}

export interface Me extends SessionUser {
  mfaSatisfied: boolean
  mfaSetupRequired: boolean
  candidateProfileId: string | null
}

export interface CandidateProfile {
  id: string
  userId: string
  firstName: string
  lastName: string
  email: string
  phone: string | null
  createdAtUtc: string
}

export type CandidateSummary = Omit<CandidateProfile, 'userId'>

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface StaffMember {
  id: string
  email: string
  firstName: string
  lastName: string
  roles: Role[]
  mfaEnabled: boolean
  isDisabled: boolean
  invitationPending: boolean
  createdAtUtc: string
  lastSignInAtUtc: string | null
}

export interface AuditEvent {
  id: number
  occurredAtUtc: string
  action: string
  outcome: 'Success' | 'Failure' | 'Denied'
  actorUserId: string | null
  actorEmail: string | null
  entityType: string | null
  entityId: string | null
  ipAddress: string | null
  details: string | null
}

export interface MfaSetup {
  sharedKey: string
  authenticatorUri: string
}

/** A failed request, carrying the server's user-facing message and any per-field messages. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }

  /** Field messages keyed the way forms name fields (the server uses PascalCase property names). */
  fieldMessage(field: string): string | undefined {
    const key = Object.keys(this.fieldErrors).find((k) => k.toLowerCase() === field.toLowerCase())
    return key ? this.fieldErrors[key][0] : undefined
  }
}

let accessToken: string | null = null
let refreshing: Promise<Session | null> | null = null
let onSessionChange: (session: Session | null) => void = () => {}

export function setSessionListener(listener: (session: Session | null) => void) {
  onSessionChange = listener
}

export function setAccessToken(token: string | null) {
  accessToken = token
}

/** Exchanges the refresh cookie for a new session. Concurrent callers share one request, since each refresh rotates the cookie. */
export function refreshSession(): Promise<Session | null> {
  refreshing ??= fetch('/api/auth/refresh', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'X-Nichotan-Client': 'web' },
  })
    .then(async (response) => {
      const session = response.ok ? ((await response.json()) as Session) : null
      accessToken = session?.accessToken ?? null
      onSessionChange(session)
      return session
    })
    .catch(() => null)
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

async function toError(response: Response): Promise<ApiError> {
  let message = response.status === 429 ? 'Too many attempts. Please wait a few minutes and try again.' : 'Something went wrong. Please try again.'
  let fieldErrors: Record<string, string[]> = {}
  try {
    const problem = (await response.json()) as { detail?: string; title?: string; errors?: Record<string, string[]> }
    message = problem.detail ?? problem.title ?? message
    fieldErrors = problem.errors ?? {}
  } catch {
    // Not a problem-details body; keep the generic message.
  }
  return new ApiError(response.status, message, fieldErrors)
}

async function send(method: string, path: string, body?: unknown): Promise<Response> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`
  return fetch(path, { method, headers, credentials: 'same-origin', body: body === undefined ? undefined : JSON.stringify(body) })
}

/** Calls the API. Auth endpoints are never retried, so a wrong password isn't mistaken for an expired token. */
export async function api<T = void>(method: string, path: string, body?: unknown): Promise<T> {
  let response = await send(method, path, body)
  if (response.status === 401 && accessToken && !path.startsWith('/api/auth/login')) {
    const session = await refreshSession()
    if (session) response = await send(method, path, body)
  }
  if (!response.ok) throw await toError(response)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Sends a form with a file (multipart). The browser sets the content type, so no JSON header is added. */
export async function postForm<T>(path: string, form: FormData): Promise<T> {
  const attempt = () => fetch(path, { method: 'POST', headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {}, credentials: 'same-origin', body: form })
  let response = await attempt()
  if (response.status === 401 && accessToken) {
    const session = await refreshSession()
    if (session) response = await attempt()
  }
  if (!response.ok) throw await toError(response)
  return (await response.json()) as T
}

/** Downloads a file the API returns (such as a PDF) and hands it to the browser, sending the sign-in token a plain link couldn't. */
export async function download(path: string, fileName: string): Promise<void> {
  let response = await send('GET', path)
  if (response.status === 401 && accessToken) {
    const session = await refreshSession()
    if (session) response = await send('GET', path)
  }
  if (!response.ok) throw await toError(response)
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

export const get = <T>(path: string) => api<T>('GET', path)
export const post = <T = void>(path: string, body?: unknown) => api<T>('POST', path, body)
export const put = <T>(path: string, body: unknown) => api<T>('PUT', path, body)
export const del = <T = void>(path: string) => api<T>('DELETE', path)

export function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  const text = search.toString()
  return text ? `?${text}` : ''
}
