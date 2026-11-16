import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Check, Copy, Download, KeyRound, Loader2, ShieldAlert, ShieldCheck, Smartphone } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { ResendVerification } from '@/components/resend-verification'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, get, post, refreshSession, type MfaSetup } from '@/lib/api'
import { useAuth } from '@/lib/auth'

const sixDigits = /^\d{6}$/

/** Reads a 6-digit code and posts it; shared by enable, disable and new recovery codes. */
function useCodeForm<T>(send: (code: string) => Promise<T>, onDone: (result: T) => void) {
  const [formError, setFormError] = useState<string | null>(null)
  const form = useForm<{ code: string }>({ defaultValues: { code: '' } })
  const submit = form.handleSubmit(async ({ code }) => {
    setFormError(null)
    code = code.replace(/\s/g, '')
    if (!sixDigits.test(code)) return form.setError('code', { message: 'Enter the 6-digit code from your authenticator app.' })
    try {
      onDone(await send(code))
      form.reset({ code: '' })
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Please try again.')
    }
  })
  return { form, submit, formError }
}

export default function Security() {
  const { state } = useAuth()
  if (state.status !== 'signedIn') return null
  const { user, mfaSetupRequired } = state.session

  return (
    <div className="grid max-w-3xl gap-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">Account security</h1>
        <p className="mt-1 text-muted-foreground">Signed in as {user.email}</p>
      </div>

      {mfaSetupRequired && (
        <div role="status" className="flex gap-3 rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900">
          <ShieldAlert className="size-5 shrink-0" aria-hidden />
          <p>
            <strong>Set up two-step verification to continue.</strong> Staff accounts hold candidates’ personal data, so they need a code from an authenticator app as
            well as a password.
          </p>
        </div>
      )}

      {!user.isStaff && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg text-ink">Email address</CardTitle>
            <CardDescription>{user.email}</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-wrap items-center justify-between gap-3">
            {user.emailVerified ? (
              <Badge variant="secondary" className="gap-1">
                <Check className="size-3" /> Confirmed
              </Badge>
            ) : (
              <>
                <span className="text-sm text-muted-foreground">Not confirmed yet. Check your inbox for our link.</span>
                <ResendVerification />
              </>
            )}
          </CardContent>
        </Card>
      )}

      {user.mfaEnabled ? <MfaManage isStaff={user.isStaff} /> : <MfaEnrol isStaff={user.isStaff} />}
    </div>
  )
}

function MfaEnrol({ isStaff }: { isStaff: boolean }) {
  const { signOut } = useAuth()
  const navigate = useNavigate()
  const [codes, setCodes] = useState<string[] | null>(null)
  const setup = useQuery({ queryKey: ['mfa', 'setup'], queryFn: () => get<MfaSetup>('/api/auth/mfa/setup'), staleTime: Infinity, gcTime: 0 })
  const { form, submit, formError } = useCodeForm(
    (code) => post<{ recoveryCodes: string[] }>('/api/auth/mfa/enable', { code }),
    (result) => setCodes(result.recoveryCodes),
  )

  const finish = async () => {
    if (isStaff) {
      // Staff features need a session that was opened with a code, so staff sign in again now.
      await signOut()
      navigate('/login?notice=mfa-enabled', { replace: true })
    } else {
      setCodes(null)
      await refreshSession()
      toast.success('Two-step verification is on.')
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg text-ink">
          <Smartphone className="size-5 text-primary" aria-hidden /> Two-step verification
        </CardTitle>
        <CardDescription>
          {isStaff ? 'Required for staff.' : 'Optional, and recommended.'} Use an authenticator app such as Google Authenticator, Microsoft Authenticator or 1Password.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {setup.isError ? (
          <FormError message="We couldn’t start the setup. Please refresh the page." />
        ) : !setup.data ? (
          <Skeleton className="h-48" />
        ) : (
          <ol className="grid gap-6">
            <li className="grid gap-2">
              <p className="font-medium text-ink">1. Add Nichotan to your authenticator app</p>
              <p className="text-sm text-muted-foreground">In the app, choose to add an account and enter this setup key (time-based):</p>
              <div className="flex flex-wrap items-center gap-2">
                <code className="rounded-lg bg-muted px-3 py-2 font-mono text-sm tracking-wider break-all text-ink">{setup.data.sharedKey}</code>
                <CopyButton text={setup.data.sharedKey.replace(/\s/g, '')} label="Copy key" />
              </div>
              <p className="text-sm text-muted-foreground">
                On the phone with the app? <a href={setup.data.authenticatorUri} className="font-medium text-primary hover:underline">Open it in your authenticator</a>.
              </p>
            </li>
            <li className="grid gap-2">
              <p className="font-medium text-ink">2. Enter the 6-digit code it shows</p>
              <form onSubmit={submit} noValidate className="grid max-w-xs gap-3">
                <FormError message={formError} />
                <Field
                  label="Authentication code"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  className="[&_input]:font-mono [&_input]:text-lg [&_input]:tracking-[0.3em]"
                  error={form.formState.errors.code?.message}
                  {...form.register('code')}
                />
                <Button type="submit" className="h-10 justify-self-start px-5" disabled={form.formState.isSubmitting}>
                  {form.formState.isSubmitting ? <Loader2 className="animate-spin" /> : <ShieldCheck />}
                  Turn on
                </Button>
              </form>
            </li>
          </ol>
        )}
      </CardContent>
      <RecoveryCodesDialog codes={codes} onClose={finish} closeLabel={isStaff ? 'I’ve saved them, sign in again' : 'I’ve saved them'} />
    </Card>
  )
}

function MfaManage({ isStaff }: { isStaff: boolean }) {
  const [codes, setCodes] = useState<string[] | null>(null)
  const [mode, setMode] = useState<'codes' | 'disable' | null>(null)
  const regenerate = useCodeForm(
    (code) => post<{ recoveryCodes: string[] }>('/api/auth/mfa/recovery-codes', { code }),
    (result) => {
      setMode(null)
      setCodes(result.recoveryCodes)
    },
  )
  const disable = useCodeForm(
    (code) => post('/api/auth/mfa/disable', { code }),
    async () => {
      setMode(null)
      await refreshSession()
      toast.success('Two-step verification is off.')
    },
  )
  const active = mode === 'codes' ? regenerate : mode === 'disable' ? disable : null

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg text-ink">
          <ShieldCheck className="size-5 text-primary" aria-hidden /> Two-step verification is on
        </CardTitle>
        <CardDescription>Signing in asks for a code from your authenticator app.</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-4">
        {!active ? (
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" onClick={() => setMode('codes')}>
              <KeyRound /> New recovery codes
            </Button>
            {/* Staff must keep it on; the server refuses too. */}
            {!isStaff && (
              <Button variant="destructive" onClick={() => setMode('disable')}>
                Turn off
              </Button>
            )}
          </div>
        ) : (
          <form onSubmit={active.submit} noValidate className="grid max-w-sm gap-3">
            <p className="text-sm text-muted-foreground">
              {mode === 'codes' ? 'New codes replace your old ones. Enter a code from your app to confirm.' : 'Enter a code from your app to turn two-step verification off.'}
            </p>
            <FormError message={active.formError} />
            <Field
              key={mode}
              label="Authentication code"
              inputMode="numeric"
              autoComplete="one-time-code"
              autoFocus
              error={active.form.formState.errors.code?.message}
              {...active.form.register('code')}
            />
            <div className="flex gap-2">
              <Button type="submit" variant={mode === 'disable' ? 'destructive' : 'default'} disabled={active.form.formState.isSubmitting}>
                {active.form.formState.isSubmitting && <Loader2 className="animate-spin" />}
                {mode === 'codes' ? 'Create new codes' : 'Turn off'}
              </Button>
              <Button type="button" variant="ghost" onClick={() => setMode(null)}>
                Cancel
              </Button>
            </div>
          </form>
        )}
      </CardContent>
      <RecoveryCodesDialog codes={codes} onClose={() => setCodes(null)} closeLabel="I’ve saved them" />
    </Card>
  )
}

function RecoveryCodesDialog({ codes, onClose, closeLabel }: { codes: string[] | null; onClose: () => void; closeLabel: string }) {
  const text = codes?.join('\n') ?? ''
  const download = () => {
    const url = URL.createObjectURL(new Blob([`Nichotan recovery codes\nEach code works once.\n\n${text}\n`], { type: 'text/plain' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'nichotan-recovery-codes.txt'
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    // Closing only through the button, so nobody dismisses the codes by accident.
    <Dialog open={codes !== null}>
      <DialogContent showCloseButton={false} onEscapeKeyDown={(e) => e.preventDefault()} onPointerDownOutside={(e) => e.preventDefault()}>
        <DialogHeader>
          <DialogTitle>Save your recovery codes</DialogTitle>
          <DialogDescription>If you lose your phone, each of these codes lets you sign in once. Keep them somewhere safe. They won’t be shown again.</DialogDescription>
        </DialogHeader>
        <ul className="grid grid-cols-2 gap-2 rounded-lg bg-muted p-4 font-mono text-sm text-ink">
          {codes?.map((code) => <li key={code}>{code}</li>)}
        </ul>
        <div className="flex gap-2">
          <CopyButton text={text} label="Copy" />
          <Button variant="outline" size="sm" onClick={download}>
            <Download /> Download
          </Button>
        </div>
        <DialogFooter>
          <Button onClick={onClose}>{closeLabel}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function CopyButton({ text, label }: { text: string; label: string }) {
  const [copied, setCopied] = useState(false)
  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(text)
          setCopied(true)
          setTimeout(() => setCopied(false), 2000)
        } catch {
          toast.error('Copying isn’t available here. Select the text and copy it instead.')
        }
      }}
    >
      {copied ? <Check /> : <Copy />}
      {copied ? 'Copied' : label}
    </Button>
  )
}
