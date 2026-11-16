import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { CheckCircle2, Loader2, MailCheck, XCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Field, FormError, PasswordField } from '@/components/form'
import { applyServerErrors, passwordRule } from '@/lib/form-errors'
import { AuthLayout } from '@/layouts/AuthLayout'
import { ApiError, post, refreshSession } from '@/lib/api'
import { useAuth } from '@/lib/auth'
import { email, newPassword } from '@/lib/schemas'

function BackToSignIn() {
  return (
    <Link to="/login" className="font-medium text-primary hover:underline">
      Back to sign in
    </Link>
  )
}

function Outcome({ ok, children }: { ok: boolean; children: React.ReactNode }) {
  const Icon = ok ? CheckCircle2 : XCircle
  return (
    <div className="flex flex-col items-center gap-3 text-center">
      <Icon className={ok ? 'size-12 text-primary' : 'size-12 text-destructive'} aria-hidden />
      <div className="text-sm text-muted-foreground">{children}</div>
    </div>
  )
}

// ─────────────────────────── /verify-email?token= ───────────────────────────

export function VerifyEmail() {
  const [params] = useSearchParams()
  const { state } = useAuth()
  const token = params.get('token') ?? ''
  const [result, setResult] = useState<{ ok: boolean; message: string } | null>(null)
  const sent = useRef(false)

  useEffect(() => {
    // Links work once, so the request is sent once even when React runs effects twice in development.
    if (sent.current) return
    sent.current = true
    post('/api/auth/verify-email', { token })
      .then(async () => {
        // If this browser is signed in, its session still says "unverified": pick up the change.
        await refreshSession()
        setResult({ ok: true, message: 'Your email address is confirmed. Thank you.' })
      })
      .catch((error: unknown) => setResult({ ok: false, message: error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Please try again.' }))
  }, [token])

  return (
    <AuthLayout title="Confirm your email" footer={state.status === 'signedIn' ? <Link to="/" className="font-medium text-primary hover:underline">Continue</Link> : <BackToSignIn />}>
      {result ? (
        <Outcome ok={result.ok}>
          {result.message}
          {!result.ok && <p className="mt-2">You can ask for a new link from your account once you sign in.</p>}
        </Outcome>
      ) : (
        <div className="flex justify-center py-6">
          <Loader2 className="size-8 animate-spin text-muted-foreground" aria-label="Confirming" />
        </div>
      )}
    </AuthLayout>
  )
}

// ─────────────────────────── /forgot-password ───────────────────────────

export function ForgotPassword() {
  const [sentTo, setSentTo] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<{ email: string }>({ resolver: zodResolver(z.object({ email })), defaultValues: { email: '' } })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async (values) => {
    setFormError(null)
    try {
      await post('/api/auth/forgot-password', values)
      setSentTo(values.email)
    } catch (error) {
      setFormError(applyServerErrors(error, ['email'], form.setError))
    }
  })

  return (
    <AuthLayout title="Reset your password" subtitle={sentTo ? undefined : 'We’ll email you a link to choose a new one.'} footer={<BackToSignIn />}>
      {sentTo ? (
        <div className="flex flex-col items-center gap-3 text-center">
          <MailCheck className="size-12 text-primary" aria-hidden />
          <p className="text-sm text-muted-foreground">
            If <strong className="text-foreground">{sentTo}</strong> has a Nichotan account, a reset link is on its way. It works for one hour.
          </p>
        </div>
      ) : (
        <form onSubmit={submit} noValidate className="grid gap-4">
          <FormError message={formError} />
          <Field label="Email address" type="email" autoComplete="email" error={errors.email?.message} {...form.register('email')} />
          <Button type="submit" size="lg" className="h-11 text-base" disabled={isSubmitting}>
            {isSubmitting && <Loader2 className="animate-spin" />}
            Send reset link
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}

// ─────────────── /reset-password?token= and /accept-invitation?token= ───────────────

type PasswordValues = z.infer<typeof newPassword>

function SetPasswordForm({ endpoint, field, submitLabel, onDone }: { endpoint: string; field: 'newPassword' | 'password'; submitLabel: string; onDone: () => void }) {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<PasswordValues>({ resolver: zodResolver(newPassword), defaultValues: { password: '', confirm: '' } })
  const { errors, isSubmitting } = form.formState

  const submit = form.handleSubmit(async ({ password }) => {
    setFormError(null)
    try {
      await post(endpoint, { token, [field]: password })
      onDone()
    } catch (error) {
      if (error instanceof ApiError) {
        const message = error.fieldMessage(field)
        if (message) return form.setError('password', { message })
      }
      setFormError(applyServerErrors(error, [], form.setError))
    }
  })

  if (!token) return <Outcome ok={false}>This link is incomplete. Open it again from your email, or ask for a new one.</Outcome>

  return (
    <form onSubmit={submit} noValidate className="grid gap-4">
      <FormError message={formError} />
      <PasswordField label="New password" autoComplete="new-password" hint={passwordRule} error={errors.password?.message} {...form.register('password')} />
      <PasswordField label="Confirm new password" autoComplete="new-password" error={errors.confirm?.message} {...form.register('confirm')} />
      <Button type="submit" size="lg" className="h-11 text-base" disabled={isSubmitting}>
        {isSubmitting && <Loader2 className="animate-spin" />}
        {submitLabel}
      </Button>
    </form>
  )
}

export function ResetPassword() {
  const navigate = useNavigate()
  const { state, signOut } = useAuth()
  return (
    <AuthLayout title="Choose a new password" subtitle="Changing it signs you out everywhere else too." footer={<BackToSignIn />}>
      <SetPasswordForm
        endpoint="/api/auth/reset-password"
        field="newPassword"
        submitLabel="Change password"
        onDone={async () => {
          if (state.status === 'signedIn') await signOut()
          navigate('/login?notice=password-reset', { replace: true })
        }}
      />
    </AuthLayout>
  )
}

export function AcceptInvitation() {
  const navigate = useNavigate()
  return (
    <AuthLayout title="Join the Nicho Tan team" subtitle="Choose a password for your staff account. Next you’ll set up an authenticator app.">
      <SetPasswordForm
        endpoint="/api/auth/accept-invitation"
        field="password"
        submitLabel="Create my account"
        onDone={() => navigate('/login?notice=invitation-accepted', { replace: true })}
      />
    </AuthLayout>
  )
}
