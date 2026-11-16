import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, refreshSession, setAccessToken } from './api'
import { safeNext } from './navigation'

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const session = (token: string) => ({ accessToken: token, accessTokenExpiresAtUtc: '', user: {}, mfaSatisfied: false, mfaSetupRequired: false })

describe('api client', () => {
  const fetchMock = vi.fn<typeof fetch>()
  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    setAccessToken(null)
  })
  afterEach(() => {
    fetchMock.mockReset()
    vi.unstubAllGlobals()
  })

  it('sends the access token and, after a 401, refreshes once and retries', async () => {
    setAccessToken('old')
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json(200, session('new')))
      .mockResolvedValueOnce(json(200, { ok: true }))

    await expect(api('GET', '/api/thing')).resolves.toEqual({ ok: true })

    const headers = (call: number) => fetchMock.mock.calls[call][1]?.headers as Record<string, string>
    expect(headers(0).Authorization).toBe('Bearer old')
    expect(fetchMock.mock.calls[1][0]).toBe('/api/auth/refresh')
    expect(headers(1)['X-Nichotan-Client']).toBe('web')
    expect(headers(2).Authorization).toBe('Bearer new')
  })

  it('does not treat a failed sign-in as an expired token', async () => {
    setAccessToken('token')
    fetchMock.mockResolvedValueOnce(json(401, { detail: 'Email or password is incorrect.' }))
    await expect(api('POST', '/api/auth/login', {})).rejects.toThrow('Email or password is incorrect.')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('shares one refresh between simultaneous callers, since each refresh rotates the cookie', async () => {
    fetchMock.mockResolvedValue(json(200, session('t')))
    const [a, b] = await Promise.all([refreshSession(), refreshSession()])
    expect(a).toBe(b)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('turns problem details into an ApiError with field messages', async () => {
    fetchMock.mockResolvedValueOnce(json(400, { title: 'One or more fields are invalid.', errors: { FirstName: ['Required.'] } }))
    const error = await api('POST', '/api/x', {}).catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldMessage('firstName')).toBe('Required.')
  })
})

describe('safeNext', () => {
  it('follows only same-site paths', () => {
    expect(safeNext('/staff/candidates?page=2')).toBe('/staff/candidates?page=2')
    expect(safeNext('//evil.example')).toBeNull()
    expect(safeNext('/\\evil.example')).toBeNull()
    expect(safeNext('https://evil.example')).toBeNull()
    expect(safeNext(null)).toBeNull()
  })
})
