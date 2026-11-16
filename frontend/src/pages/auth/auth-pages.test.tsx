import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { AuthContext, type AuthContextValue } from '@/lib/auth'
import Login from './Login'
import Register from './Register'

const fetchMock = vi.fn<typeof fetch>()
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function renderAt(path: string, element: ReactNode, auth: Partial<AuthContextValue> = {}) {
  const value: AuthContextValue = { state: { status: 'signedOut', session: null }, begin: vi.fn(), signOut: vi.fn(), ...auth }
  render(
    <AuthContext value={value}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path={path.split('?')[0]} element={element} />
          <Route path="/portal" element={<p>Portal home</p>} />
          <Route path="/staff/team" element={<p>Team page</p>} />
        </Routes>
      </MemoryRouter>
    </AuthContext>,
  )
  return value
}

beforeEach(() => vi.stubGlobal('fetch', fetchMock))
afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

describe('Login', () => {
  it('shows the server’s message when sign-in fails', async () => {
    fetchMock.mockResolvedValueOnce(json(401, { detail: 'Email or password is incorrect.' }))
    renderAt('/login', <Login />)
    await userEvent.type(screen.getByLabelText('Email address'), 'a@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'Wrong-Pass-123')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.')
  })

  it('asks for an authenticator code when the account has one, then follows ?next', async () => {
    const session = { accessToken: 't', accessTokenExpiresAtUtc: '', user: { isStaff: true }, mfaSatisfied: true, mfaSetupRequired: false }
    fetchMock.mockResolvedValueOnce(json(200, { mfaRequired: true, mfaChallengeToken: 'challenge', session: null })).mockResolvedValueOnce(json(200, session))
    const auth = renderAt('/login?next=/staff/team', <Login />)

    await userEvent.type(screen.getByLabelText('Email address'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'Admin-Pass-123')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    const code = await screen.findByLabelText('Authentication code')
    await userEvent.click(screen.getByRole('button', { name: 'Verify and sign in' }))
    expect(await screen.findByText('Enter the 6-digit code.')).toBeInTheDocument()

    await userEvent.type(code, '123 456')
    await userEvent.click(screen.getByRole('button', { name: 'Verify and sign in' }))
    expect(await screen.findByText('Team page')).toBeInTheDocument()
    expect(JSON.parse(fetchMock.mock.calls[1][1]?.body as string)).toEqual({ challengeToken: 'challenge', code: '123456', isRecoveryCode: false })
    expect(auth.begin).toHaveBeenCalledWith(session)
  })
})

describe('Register', () => {
  it('checks the form before sending anything', async () => {
    renderAt('/register', <Register />)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByText('Enter your first name.')).toBeInTheDocument()
    expect(screen.getByText('Enter your email address.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('puts the server’s field messages on the right fields', async () => {
    fetchMock.mockResolvedValueOnce(json(400, { title: 'One or more fields are invalid.', errors: { Email: ['An account with this email already exists.'] } }))
    renderAt('/register', <Register />)
    await userEvent.type(screen.getByLabelText(/First name/), 'Thandiwe')
    await userEvent.type(screen.getByLabelText(/Last or family name/), 'Moyo')
    await userEvent.type(screen.getByLabelText(/Email address/), 'taken@example.com')
    await userEvent.type(screen.getByLabelText(/^Password/), 'Candidate-Pass-2026')
    await userEvent.type(screen.getByLabelText(/Confirm password/), 'Candidate-Pass-2026')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByText('An account with this email already exists.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Email address/)).toHaveAttribute('aria-invalid', 'true')
  })
})
