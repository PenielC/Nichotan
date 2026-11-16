import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowRight, FileText, MailWarning, Phone, ShieldCheck, UserRound } from 'lucide-react'
import { AdmissionStatusBadge } from '@/components/admission-status'
import { FormStatusBadge } from '@/components/application-summary'
import { Journey } from '@/components/journey'
import { ResendVerification } from '@/components/resend-verification'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { candidateStatusLabel, dayText, intakeText, type MyAdmission } from '@/lib/admissions'
import { get, type CandidateProfile } from '@/lib/api'
import { normalisePhone } from '@/lib/phone'
import { applicationKey, type ApplicationFormResult } from '@/lib/application'
import { useAuth } from '@/lib/auth'

export default function PortalHome() {
  const { state } = useAuth()
  if (state.status !== 'signedIn') return null
  const { user } = state.session

  return (
    <div className="grid gap-6">
      <div>
        <h1 className="text-2xl font-bold text-ink sm:text-3xl">Hello, {user.firstName}</h1>
        <p className="mt-1 text-muted-foreground">Track your study abroad application here.</p>
      </div>

      {!user.emailVerified && (
        <Card className="border-l-4 border-l-amber-500">
          <CardContent className="flex flex-wrap items-center gap-4">
            <MailWarning className="size-6 shrink-0 text-amber-600" aria-hidden />
            <div className="min-w-0 flex-1">
              <p className="font-medium text-ink">Confirm your email address</p>
              <p className="text-sm text-muted-foreground">We sent a link to {user.email}. Confirming it lets us reach you about your application.</p>
            </div>
            <ResendVerification />
          </CardContent>
        </Card>
      )}

      <PhoneNeedsCountryCode />
      <ApplicationCard />
      <MyApplications />

      <Card>
        <CardHeader>
          <CardTitle className="text-lg text-ink">Your journey</CardTitle>
          <CardDescription>Your application hasn’t started yet. Once it does, each stage is tracked here, and your counsellor will guide you through it.</CardDescription>
        </CardHeader>
        <CardContent>
          <Journey current={null} />
        </CardContent>
      </Card>

      <div className="grid gap-4 sm:grid-cols-2">
        <NextStep icon={UserRound} title="Check your details" text="Make sure your name and phone number are right, so we can reach you." to="/portal/profile" action="My details" />
        <NextStep
          icon={ShieldCheck}
          title={user.mfaEnabled ? 'Two-step verification is on' : 'Protect your account'}
          text={user.mfaEnabled ? 'Your account asks for a code from your authenticator app when you sign in.' : 'Add two-step verification with an authenticator app. It’s optional, and recommended.'}
          to="/account/security"
          action={user.mfaEnabled ? 'Manage' : 'Set it up'}
        />
      </div>
    </div>
  )
}

/** Numbers saved before the country-code rule are left alone, so ask the student to fix theirs. It shares its query with My details, so saving there clears it. */
function PhoneNeedsCountryCode() {
  const profile = useQuery({ queryKey: ['candidates', 'me'], queryFn: () => get<CandidateProfile>('/api/candidates/me') })
  const saved = profile.data?.phone
  if (!saved?.trim() || normalisePhone(saved)) return null
  return (
    <Card className="border-l-4 border-l-amber-500">
      <CardContent className="flex flex-wrap items-center gap-4">
        <Phone className="size-6 shrink-0 text-amber-600" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="font-medium text-ink">Add your country code to your phone number</p>
          <p className="text-sm text-muted-foreground">We have {saved} on file. Start it with your country code, for example +263 77 123 4567, so we can reach you on WhatsApp.</p>
        </div>
        <Button asChild>
          <Link to="/portal/profile">
            Fix my number <ArrowRight />
          </Link>
        </Button>
      </CardContent>
    </Card>
  )
}

function NextStep({ icon: Icon, title, text, to, action }: { icon: typeof UserRound; title: string; text: string; to: string; action: string }) {
  return (
    <Card>
      <CardContent className="flex h-full flex-col gap-3">
        <span className="flex size-10 items-center justify-center rounded-lg bg-accent text-primary">
          <Icon className="size-5" aria-hidden />
        </span>
        <div className="flex-1">
          <p className="font-semibold text-ink">{title}</p>
          <p className="mt-1 text-sm text-muted-foreground">{text}</p>
        </div>
        <Button asChild variant="outline" className="self-start">
          <Link to={to}>
            {action}
            <ArrowRight />
          </Link>
        </Button>
      </CardContent>
    </Card>
  )
}

function ApplicationCard() {
  const form = useQuery({ queryKey: applicationKey, queryFn: () => get<ApplicationFormResult>('/api/application-form') })
  const status = form.data?.status ?? 'Draft'
  const started = form.data?.id != null
  const copy =
    status === 'Submitted'
      ? 'Your form is signed and with your counsellor. You can download a copy any time.'
      : status === 'Reopened'
        ? 'Your counsellor has asked for a few changes. Open the form to see what to update.'
        : started
          ? 'Your draft is saved. Pick up where you left off, then review and sign.'
          : 'Start your Nichotan application form: your details, the programs you want, and your schooling so far.'
  return (
    <Card className={status === 'Reopened' ? 'border-l-4 border-l-destructive' : undefined}>
      <CardContent className="flex flex-wrap items-center gap-4">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-accent text-primary">
          <FileText className="size-5" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <p className="flex flex-wrap items-center gap-2 font-semibold text-ink">
            My application form {form.data && (started || status !== 'Draft') && <FormStatusBadge status={status} />}
          </p>
          <p className="mt-1 text-sm text-muted-foreground">{copy}</p>
        </div>
        <Button asChild variant={status === 'Submitted' ? 'outline' : 'default'}>
          <Link to="/portal/application">
            {status === 'Submitted' ? 'View' : started ? 'Continue' : 'Start'}
            <ArrowRight />
          </Link>
        </Button>
      </CardContent>
    </Card>
  )
}

function MyApplications() {
  const list = useQuery({ queryKey: ['admissions', 'mine'], queryFn: () => get<MyAdmission[]>('/api/admissions/mine') })
  if (!list.data) return null
  if (list.data.length === 0) {
    return (
      <Card>
        <CardContent className="flex flex-wrap items-center gap-4">
          <div className="min-w-0 flex-1">
            <p className="font-semibold text-ink">Choose where to apply</p>
            <p className="mt-1 text-sm text-muted-foreground">Pick the schools and programmes you want. Your counsellor then applies for you.</p>
          </div>
          <Button asChild>
            <Link to="/portal/applications">
              Add an application <ArrowRight />
            </Link>
          </Button>
        </CardContent>
      </Card>
    )
  }
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg text-ink">Your applications</CardTitle>
        <CardDescription>The schools and programmes you’ve chosen. Updates appear here as your counsellor applies for you and the schools reply.</CardDescription>
      </CardHeader>
      <CardContent className="grid gap-3">
        <Button asChild variant="outline" className="justify-self-start">
          <Link to="/portal/applications">
            Manage my applications <ArrowRight />
          </Link>
        </Button>
        {list.data.map((a) => (
          <div key={a.id} className="flex flex-wrap items-start justify-between gap-2 rounded-lg border p-3">
            <div className="min-w-0">
              <p className="font-medium text-ink">{a.institutionName}</p>
              <p className="text-sm text-muted-foreground">
                {a.programmeName} · {a.country} · {intakeText(a.intakeTerm, a.intakeYear)}
              </p>
              {a.submittedToInstitutionOn && <p className="mt-1 text-xs text-muted-foreground">Submitted to the school on {dayText(a.submittedToInstitutionOn)}</p>}
            </div>
            <AdmissionStatusBadge status={a.status} label={candidateStatusLabel[a.status]} />
          </div>
        ))}
      </CardContent>
    </Card>
  )
}
