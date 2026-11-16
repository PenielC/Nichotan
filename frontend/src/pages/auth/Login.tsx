import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Loader2, Mail, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Field, FormError, PasswordField } from '@/components/form'
import { AuthLayout } from '@/layouts/AuthLayout'
import { ApiError, post, type LoginResponse, type Session } from '@/lib/api'
import { homePath, useAuth } from '@/lib/auth'
import { safeNext } from '@/lib/navigation'
import { email } from '@/lib/schemas'

const credentialsSchema = z.object({ email, password: z.string().min(1, 'Enter your password.') })
type Credentials = z.infer<typeof credentialsSchema>

export default function Login() {
  const { begin } = useAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [challenge, setChallenge] = useState<string | null>(null)

  const finish = (session: Session) => {
    begin(session)
    const next = safeNext(params.get('next'))
    navigate(next && !session.mfaSetupRequired ? next : homePath(session), { replace: true })
  }

  return challenge ? (
    <MfaStep challenge={challenge} onDone={finish} onRestart={() => setChallenge(null)} />
  ) : (
    <PasswordStep onSession={finish} onChallenge={setChallenge} notice={params.get('notice')} />
  )
}

const notices: Record<string, string> = {
  'password-reset': 'Your password has been changed. Sign in with your new password.',
  'invitation-accepted': 'Your account is ready. Sign in to set up your authenticator app.',
  'mfa-enabled': 'Your authenticator app is set up. Sign in again with a code to open staff features.',
  'signed-out': 'You have been signed out.',
}

function PasswordStep({ onSession, onChallenge, notice }: { onSession: (s: Session) => void; onChallenge: (token: string) => void; notice: string | null }) {
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<Credentials>({ resolver: zodResolver(credentialsSchema), defaultValues: { email: '', password: '' } })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async (values) => {
    setFormError(null)
    try {
      const result = await post<LoginResponse>('/api/auth/login', values)
      if (result.mfaRequired && result.mfaChallengeToken) onChallenge(result.mfaChallengeToken)
      else if (result.session) onSession(result.session)
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Check your connection and try again.')
    }
  })

  return (
    <AuthLayout
      title="Welcome back"
      subtitle="Sign in to your account"
      footer={<>Your journey to study abroad starts here.</>}
    >
      {notice && notices[notice] && <p className="mb-5 rounded-lg bg-accent px-3 py-2.5 text-sm text-accent-foreground">{notices[notice]}</p>}
      <form onSubmit={submit} noValidate className="grid gap-4">
        <FormError message={formError} />
        <Field label="Email address" type="email" autoComplete="username" error={errors.email?.message} {...form.register('email')} />
        <PasswordField label="Password" autoComplete="current-password" error={errors.password?.message} {...form.register('password')} />
        <div className="-mt-1 text-right text-sm">
          <Link to="/forgot-password" className="font-medium text-primary hover:underline">
            Forgot password?
          </Link>
        </div>
        <Button type="submit" size="lg" className="h-11 text-base" disabled={isSubmitting}>
          {isSubmitting && <Loader2 className="animate-spin" />}
          Sign in
        </Button>
      </form>
      <div className="my-5 flex items-center gap-3 text-xs text-muted-foreground">
        <span className="h-px flex-1 bg-border" />
        OR
        <span className="h-px flex-1 bg-border" />
      </div>
      <Button asChild variant="secondary" size="lg" className="h-11 w-full text-base">
        <Link to="/register">
          <Mail />
          Create an account
        </Link>
      </Button>
    </AuthLayout>
  )
}

function MfaStep({ challenge, onDone, onRestart }: { challenge: string; onDone: (s: Session) => void; onRestart: () => void }) {
  const [useRecovery, setUseRecovery] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<{ code: string }>({ defaultValues: { code: '' } })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async ({ code }) => {
    setFormError(null)
    code = code.replace(/\s/g, '')
    if (!useRecovery && !/^\d{6}$/.test(code)) return form.setError('code', { message: 'Enter the 6-digit code.' })
    if (useRecovery && !code) return form.setError('code', { message: 'Enter one of your recovery codes.' })
    try {
      onDone(await post<Session>('/api/auth/login/mfa', { challengeToken: challenge, code, isRecoveryCode: useRecovery }))
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Check your connection and try again.')
      form.reset({ code: '' })
    }
  })

  return (
    <AuthLayout title="Two-step verification" subtitle={useRecovery ? 'Enter one of the recovery codes you saved.' : 'Enter the 6-digit code from your authenticator app.'}>
      <form onSubmit={submit} noValidate className="grid gap-4">
        <FormError message={formError} />
        <Field
          key={useRecovery ? 'recovery' : 'totp'}
          label={useRecovery ? 'Recovery code' : 'Authentication code'}
          autoComplete="one-time-code"
          inputMode={useRecovery ? 'text' : 'numeric'}
          autoFocus
          className="[&_input]:text-center [&_input]:font-mono [&_input]:text-lg [&_input]:tracking-[0.3em]"
          error={errors.code?.message}
          {...form.register('code')}
        />
        <Button type="submit" size="lg" className="h-11 text-base" disabled={isSubmitting}>
          {isSubmitting ? <Loader2 className="animate-spin" /> : <ShieldCheck />}
          Verify and sign in
        </Button>
      </form>
      <div className="mt-5 flex flex-col items-center gap-2 text-sm">
        <button
          type="button"
          className="font-medium text-primary hover:underline"
          onClick={() => {
            setUseRecovery((v) => !v)
            setFormError(null)
            form.reset({ code: '' })
          }}
        >
          {useRecovery ? 'Use my authenticator app instead' : 'Lost your phone? Use a recovery code'}
        </button>
        <button type="button" className="text-muted-foreground hover:text-foreground" onClick={onRestart}>
          Start again
        </button>
      </div>
    </AuthLayout>
  )
}
