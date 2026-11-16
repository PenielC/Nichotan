import { useEffect, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { post, refreshSession, setAccessToken, setSessionListener, type Session } from '@/lib/api'
import { AuthContext, type AuthState } from '@/lib/auth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [state, setState] = useState<AuthState>({ status: 'loading', session: null })

  useEffect(() => {
    // Any refresh (here or after a 401 deep in the app) updates who is signed in.
    setSessionListener((session) => setState(session ? { status: 'signedIn', session } : { status: 'signedOut', session: null }))
    // A returning visitor may still hold a refresh cookie: try it once on load.
    void refreshSession()
    return () => setSessionListener(() => {})
  }, [])

  const begin = (session: Session) => {
    setAccessToken(session.accessToken)
    queryClient.clear()
    setState({ status: 'signedIn', session })
  }

  const signOut = async () => {
    try {
      await post('/api/auth/logout')
    } finally {
      setAccessToken(null)
      queryClient.clear()
      setState({ status: 'signedOut', session: null })
    }
  }

  return <AuthContext value={{ state, begin, signOut }}>{children}</AuthContext>
}
