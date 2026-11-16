import { useState, type ComponentProps } from 'react'
import { PhoneHint } from '@/components/phone-hint'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, ArrowRight, CheckCircle2, Download, Loader2, Save } from 'lucide-react'
import { toast } from 'sonner'
import { FormStatusBadge, FormSummary } from '@/components/application-summary'
import { Field, FormError, SelectField } from '@/components/form'
import { SignaturePad } from '@/components/signature-pad'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, download, get, post, put } from '@/lib/api'
import {
  applicantLevelLabel,
  applicantLevelOf,
  applicationKey,
  destinations,
  genders,
  highSchoolGrades,
  normaliseServerErrors,
  programLevels,
  startTerms,
  stepOf,
  toFormState,
  toRequest,
  validateApplication,
  type ApplicationFormData,
  type ApplicationFormResult,
  type FormErrors,
  type FormStep,
} from '@/lib/application'
import { formatDate, formatDateTime } from '@/lib/format'

const thisYear = new Date().getFullYear()
const today = new Date().toISOString().slice(0, 10)

const steps: { id: FormStep; label: string }[] = [
  { id: 'personal', label: 'Personal' },
  { id: 'program', label: 'Program' },
  { id: 'academic', label: 'Academic' },
  { id: 'sign', label: 'Review & sign' },
]

export default function Application() {
  const form = useQuery({ queryKey: applicationKey, queryFn: () => get<ApplicationFormResult>('/api/application-form') })

  return (
    <div className="grid gap-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">My application form</h1>
        <p className="mt-1 text-muted-foreground">Tell us about you and where you want to study. You can save and come back at any time.</p>
      </div>
      {form.isError ? (
        <FormError message="We couldn’t load your form. Please refresh the page." />
      ) : !form.data ? (
        <Skeleton className="h-96" />
      ) : form.data.isEditable ? (
        // Re-created after a reopen, so the form starts from the saved copy.
        <Wizard key={`${form.data.status}-${form.data.currentVersion}`} form={form.data} />
      ) : (
        <Submitted form={form.data} />
      )}
    </div>
  )
}

// ─────────────────────────── Locked: submitted ───────────────────────────

function Submitted({ form }: { form: ApplicationFormResult }) {
  const [error, setError] = useState<string | null>(null)
  const pdf = async (n: number) => {
    setError(null)
    try {
      await download(`/api/application-form/versions/${n}/pdf`, `Nichotan-application-form-v${n}.pdf`)
    } catch {
      setError('We couldn’t download the PDF. Please try again.')
    }
  }
  const latest = form.versions[form.versions.length - 1]

  return (
    <>
      <Alert>
        <CheckCircle2 />
        <AlertTitle>Your form is submitted{form.submittedAtUtc ? ` on ${formatDate(form.submittedAtUtc)}` : ''}</AlertTitle>
        <AlertDescription>It’s now locked. Your counsellor will review it. If something needs correcting, they’ll reopen it and let you know.</AlertDescription>
      </Alert>
      <FormError message={error} />
      <Card>
        <CardHeader className="flex-row items-start justify-between gap-4">
          <div>
            <CardTitle className="text-lg text-ink">Signed copies</CardTitle>
            <CardDescription>Download a printable PDF of what you signed.</CardDescription>
          </div>
          <FormStatusBadge status={form.status} />
        </CardHeader>
        <CardContent className="grid gap-2">
          {[...form.versions].reverse().map((v) => (
            <div key={v.versionNumber} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2">
              <div className="text-sm">
                <span className="font-medium text-ink">Version {v.versionNumber}</span>
                <span className="text-muted-foreground">
                  {' '}
                  · signed by {v.signerName} · {formatDateTime(v.submittedAtUtc)}
                  {v === latest ? ' · current' : ''}
                </span>
              </div>
              <Button variant="outline" size="sm" onClick={() => pdf(v.versionNumber)}>
                <Download /> PDF
              </Button>
            </div>
          ))}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg text-ink">What you submitted</CardTitle>
          {form.applicantLevel && <CardDescription>Applicant type: {applicantLevelLabel[form.applicantLevel]}</CardDescription>}
        </CardHeader>
        <CardContent>
          <FormSummary data={form.data} />
        </CardContent>
      </Card>
    </>
  )
}

// ─────────────────────────── Editing ───────────────────────────

function T({ errors, path, label, value, onChange, ...rest }: { errors: FormErrors; path: string; value: string; onChange: (v: string) => void } & Omit<ComponentProps<typeof Field>, 'value' | 'onChange' | 'error'>) {
  return <Field label={label} value={value} onChange={(e) => onChange(e.target.value)} error={errors[path]} {...rest} />
}

function Wizard({ form }: { form: ApplicationFormResult }) {
  const queryClient = useQueryClient()
  const [data, setData] = useState<ApplicationFormData>(() => toFormState(form.data))
  const [step, setStep] = useState<FormStep>('personal')
  const [errors, setErrors] = useState<FormErrors>({})
  const [busy, setBusy] = useState<'save' | 'submit' | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [signerName, setSignerName] = useState('')
  const [accepted, setAccepted] = useState(false)
  const [signature, setSignature] = useState<string | null>(null)
  const [signError, setSignError] = useState<string | undefined>()

  const edit = (change: (draft: ApplicationFormData) => void) =>
    setData((current) => {
      const next = structuredClone(current)
      change(next)
      return next
    })

  const level = applicantLevelOf(data.program.programLevel)
  const problems = validateApplication(data)
  const stepIndex = steps.findIndex((s) => s.id === step)

  /** Saves the draft. Returns false (and says why) if the server refused it. */
  const save = async (): Promise<boolean> => {
    setFormError(null)
    try {
      const saved = await put<ApplicationFormResult>('/api/application-form', toRequest(data))
      queryClient.setQueryData(applicationKey, saved)
      return true
    } catch (error) {
      setFormError(error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Check your connection and try again.')
      return false
    }
  }

  const go = async (to: FormStep) => {
    setBusy('save')
    const ok = await save()
    setBusy(null)
    if (ok) {
      setStep(to)
      window.scrollTo({ top: 0, behavior: 'smooth' })
    }
  }

  const saveDraft = async () => {
    setBusy('save')
    const ok = await save()
    setBusy(null)
    if (ok) toast.success('Draft saved.')
  }

  const submit = async () => {
    setSignError(undefined)
    setErrors(problems)
    if (Object.keys(problems).length > 0) return setFormError('Some answers are missing or need fixing. See the list above.')
    if (!signature) return setSignError('Draw your signature in the box.')
    if (!signerName.trim()) return setFormError('Type your full name to sign.')
    if (!accepted) return setFormError('Tick the box to confirm the declaration.')

    setBusy('submit')
    try {
      if (!(await save())) return
      const result = await post<ApplicationFormResult>('/api/application-form/submit', { signerName: signerName.trim(), signatureImage: signature, acceptDeclaration: true, declarationVersion: form.declarationVersion })
      queryClient.setQueryData(applicationKey, result)
      toast.success('Your form is submitted.')
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        const fromServer = normaliseServerErrors(error.fieldErrors)
        setErrors(fromServer)
        const first = Object.keys(fromServer)[0]
        setStep(stepOf(first))
        setFormError('Please fix the highlighted fields, then sign again.')
      } else {
        setFormError(error instanceof ApiError ? error.message : 'We couldn’t reach Nichotan. Check your connection and try again.')
      }
    } finally {
      setBusy(null)
    }
  }

  /** A step is complete when nothing it asks for is missing. Academic can't be judged until the program level is chosen. */
  const stepComplete = (id: FormStep) => !Object.keys(problems).some((path) => stepOf(path) === id) && (id !== 'academic' || level !== null)
  const numberStyle = (id: FormStep) => (id === 'sign' ? 'bg-muted text-foreground' : stepComplete(id) ? 'bg-primary text-primary-foreground' : 'bg-destructive text-white')

  const groupedProblems =Object.entries(problems).map(([path, message]) => ({ path, message, step: stepOf(path) }))

  return (
    <>
      {form.status === 'Reopened' && (
        <Alert variant="destructive">
          <AlertTriangle />
          <AlertTitle>Your counsellor has asked for changes</AlertTitle>
          <AlertDescription>
            {form.reopenReason}
            <span className="mt-1 block text-muted-foreground">Update your answers, then review and sign again. Your earlier submission is kept on record.</span>
          </AlertDescription>
        </Alert>
      )}

      <nav aria-label="Form steps" className="grid grid-cols-4 gap-2">
        {steps.map((s, i) => (
          <button
            key={s.id}
            type="button"
            onClick={() => go(s.id)}
            disabled={busy !== null}
            aria-current={s.id === step ? 'step' : undefined}
            className={`flex flex-col items-center gap-1 rounded-lg border px-2 py-2.5 text-xs font-medium sm:flex-row sm:justify-center sm:gap-2 sm:text-sm ${s.id === step ? 'border-primary bg-accent text-accent-foreground' : 'bg-card text-muted-foreground hover:text-foreground'}`}
          >
            <span className={`flex size-6 items-center justify-center rounded-full text-xs font-semibold ${numberStyle(s.id)}`}>
              {i + 1}
              {s.id !== 'sign' && <span className="sr-only">{stepComplete(s.id) ? ' (complete)' : ' (information missing)'}</span>}
            </span>
            {s.label}
          </button>
        ))}
      </nav>

      <Card>
        <CardContent className="grid gap-6">
          <FormError message={formError} />

          {step === 'personal' && (
            <>
              <Section title="Personal information" hint="Use your names exactly as they appear in your passport.">
                <T errors={errors} path="personal.firstname" label="First name" required autoComplete="given-name" value={data.personal.firstName} onChange={(v) => edit((d) => void (d.personal.firstName = v))} />
                <T errors={errors} path="personal.middlenames" label="Middle name(s)" autoComplete="additional-name" value={data.personal.middleNames} onChange={(v) => edit((d) => void (d.personal.middleNames = v))} />
                <T errors={errors} path="personal.lastname" label="Last or family name" required autoComplete="family-name" value={data.personal.lastName} onChange={(v) => edit((d) => void (d.personal.lastName = v))} />
                <SelectField
                  label="Gender"
                  required
                  error={errors['personal.gender']}
                  options={genders}
                  value={data.personal.gender}
                  onChange={(e) => edit((d) => void (d.personal.gender = e.target.value as typeof d.personal.gender))}
                />
                <T errors={errors} path="personal.birthdate" label="Date of birth" required type="date" autoComplete="bday" max={today} value={data.personal.birthDate} onChange={(v) => edit((d) => void (d.personal.birthDate = v))} />
                <T errors={errors} path="personal.citizenshipcountry" label="Country of citizenship" required value={data.personal.citizenshipCountry} onChange={(v) => edit((d) => void (d.personal.citizenshipCountry = v))} />
              </Section>
              <Section title="Contact information">
                <T errors={errors} path="contact.homeaddress" label="Home address" required autoComplete="street-address" className="sm:col-span-2" value={data.contact.homeAddress} onChange={(v) => edit((d) => void (d.contact.homeAddress = v))} />
                <T errors={errors} path="contact.city" label="City" required autoComplete="address-level2" value={data.contact.city} onChange={(v) => edit((d) => void (d.contact.city = v))} />
                <T errors={errors} path="contact.country" label="Country" required autoComplete="country-name" value={data.contact.country} onChange={(v) => edit((d) => void (d.contact.country = v))} />
                <T errors={errors} path="contact.email" label="Email" required type="email" autoComplete="email" value={data.contact.email} onChange={(v) => edit((d) => void (d.contact.email = v))} />
                <T errors={errors} path="contact.phone" label="Phone number (include your country code)" required type="tel" inputMode="tel" autoComplete="tel" placeholder="+263 77 123 4567" hint={<PhoneHint value={data.contact.phone} />} value={data.contact.phone} onChange={(v) => edit((d) => void (d.contact.phone = v))} />
                <T errors={errors} path="contact.alternativephone" label="Alternative phone (optional, with country code)" type="tel" inputMode="tel" placeholder="+263 77 123 4567" hint={data.contact.alternativePhone.trim() ? <PhoneHint value={data.contact.alternativePhone} /> : undefined} value={data.contact.alternativePhone} onChange={(v) => edit((d) => void (d.contact.alternativePhone = v))} />
              </Section>
              <Section title="Next of kin">
                <T errors={errors} path="contact.nextofkinname" label="Full name" required value={data.contact.nextOfKinName} onChange={(v) => edit((d) => void (d.contact.nextOfKinName = v))} />
                <T errors={errors} path="contact.nextofkinemail" label="Email" required type="email" value={data.contact.nextOfKinEmail} onChange={(v) => edit((d) => void (d.contact.nextOfKinEmail = v))} />
                <T errors={errors} path="contact.nextofkinphone" label="Phone (optional, with country code)" type="tel" inputMode="tel" placeholder="+263 77 123 4567" hint={data.contact.nextOfKinPhone.trim() ? <PhoneHint value={data.contact.nextOfKinPhone} /> : undefined} value={data.contact.nextOfKinPhone} onChange={(v) => edit((d) => void (d.contact.nextOfKinPhone = v))} />
              </Section>
            </>
          )}

          {step === 'program' && (
            <>
              <Section title="Where and what you want to study">
                <SelectField
                  label="Destination country"
                  required
                  error={errors['program.destinationcountry']}
                  options={destinations.map((c) => ({ value: c, label: c }))}
                  value={data.program.destinationCountry}
                  onChange={(e) => edit((d) => void (d.program.destinationCountry = e.target.value as typeof d.program.destinationCountry))}
                />
                <SelectField
                  label="Program level"
                  required
                  error={errors['program.programlevel']}
                  options={programLevels}
                  value={data.program.programLevel}
                  onChange={(e) => edit((d) => void (d.program.programLevel = e.target.value as typeof d.program.programLevel))}
                />
                <T errors={errors} path="program.startyear" label="Year you want to start" required type="number" inputMode="numeric" min={thisYear} max={thisYear + 10} value={data.program.startYear} onChange={(v) => edit((d) => void (d.program.startYear = v))} />
                <SelectField
                  label="When do you want to start?"
                  required
                  error={errors['program.startterm']}
                  options={startTerms}
                  value={data.program.startTerm}
                  onChange={(e) => edit((d) => void (d.program.startTerm = e.target.value as typeof d.program.startTerm))}
                />
              </Section>
              <Section title="Institutions" hint="List up to 5 universities or colleges, in order of preference." error={errors['program.institutions']} columns="sm:grid-cols-1">
                {data.program.institutions.map((value, i) => (
                  <T key={i} errors={errors} path={`program.institutions.${i}`} label={`Institution ${i + 1}`} required={i === 0} value={value} onChange={(v) => edit((d) => void (d.program.institutions[i] = v))} />
                ))}
              </Section>
              <Section title="Program choices" hint="Up to 2 programmes you want to study." error={errors['program.programchoices']} columns="sm:grid-cols-1">
                {data.program.programChoices.map((value, i) => (
                  <T key={i} errors={errors} path={`program.programchoices.${i}`} label={`Program choice ${i + 1}`} required={i === 0} value={value} onChange={(v) => edit((d) => void (d.program.programChoices[i] = v))} />
                ))}
              </Section>
            </>
          )}

          {step === 'academic' && (
            <>
              <p className="text-sm text-muted-foreground">
                {!level
                  ? 'Choose your program level on the previous step so we know which sections to ask for.'
                  : level === 'Postgraduate'
                    ? 'For a postgraduate program, list the colleges or universities you have attended. High schools are optional.'
                    : 'List the high schools you have attended. Post-secondary study is optional.'}
              </p>
              <Section
                title="High schools attended, most recent first"
                hint="Up to 2 entries."
                error={errors['academic.highschools']}
                columns="sm:grid-cols-1"
              >
                {data.academic.highSchools.map((h, i) => {
                  const p = `academic.highschools.${i}`
                  return (
                    <Entry key={i} title={`High school ${i + 1}`}>
                      <T errors={errors} path={`${p}.name`} label="Name" value={h.name} onChange={(v) => edit((d) => void (d.academic.highSchools[i].name = v))} />
                      <T errors={errors} path={`${p}.provincecountry`} label="Province, Country" value={h.provinceCountry} onChange={(v) => edit((d) => void (d.academic.highSchools[i].provinceCountry = v))} />
                      <T errors={errors} path={`${p}.started`} label="Started" type="month" value={h.started} onChange={(v) => edit((d) => void (d.academic.highSchools[i].started = v))} />
                      <T errors={errors} path={`${p}.completed`} label="Completed (leave empty if still attending)" type="month" value={h.completed} onChange={(v) => edit((d) => void (d.academic.highSchools[i].completed = v))} />
                      <SelectField
                        label="Current or completed grade"
                        error={errors[`${p}.grade`]}
                        options={highSchoolGrades}
                        value={h.grade}
                        onChange={(e) => edit((d) => void (d.academic.highSchools[i].grade = e.target.value as typeof h.grade))}
                      />
                    </Entry>
                  )
                })}
              </Section>
              <Section title="Post-secondary institutions attended, most recent first" hint="Up to 3 entries." error={errors['academic.postsecondary']} columns="sm:grid-cols-1">
                {data.academic.postSecondary.map((c, i) => {
                  const p = `academic.postsecondary.${i}`
                  return (
                    <Entry key={i} title={`Institution ${i + 1}`}>
                      <T errors={errors} path={`${p}.institution`} label="Institution" value={c.institution} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].institution = v))} />
                      <T errors={errors} path={`${p}.provincecountry`} label="Province, Country" value={c.provinceCountry} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].provinceCountry = v))} />
                      <T errors={errors} path={`${p}.started`} label="Started" type="month" value={c.started} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].started = v))} />
                      <T errors={errors} path={`${p}.completed`} label="Completed (leave empty if still attending)" type="month" value={c.completed} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].completed = v))} />
                      <T errors={errors} path={`${p}.credentialawarded`} label="Credential awarded" value={c.credentialAwarded} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].credentialAwarded = v))} />
                      <T errors={errors} path={`${p}.credentialawardedon`} label="Date credential awarded" type="month" value={c.credentialAwardedOn} onChange={(v) => edit((d) => void (d.academic.postSecondary[i].credentialAwardedOn = v))} />
                    </Entry>
                  )
                })}
              </Section>
            </>
          )}

          {step === 'sign' && (
            <>
              {groupedProblems.length > 0 && (
                <Alert variant="destructive">
                  <AlertTriangle />
                  <AlertTitle>A few things need your attention before you can sign</AlertTitle>
                  <AlertDescription>
                    <ul className="mt-1 grid gap-1">
                      {groupedProblems.slice(0, 8).map((p) => (
                        <li key={p.path}>
                          <button type="button" className="text-left underline underline-offset-2" onClick={() => go(p.step)}>
                            {steps.find((s) => s.id === p.step)?.label}: {fieldName(p.path)}
                          </button>
                        </li>
                      ))}
                      {groupedProblems.length > 8 && <li>…and {groupedProblems.length - 8} more.</li>}
                    </ul>
                  </AlertDescription>
                </Alert>
              )}
              <div>
                <h2 className="text-lg font-semibold text-ink">Review your answers</h2>
                {level && <p className="text-sm text-muted-foreground">Applicant type: {applicantLevelLabel[level]}</p>}
              </div>
              <FormSummary data={data} />
              <section className="grid gap-4 rounded-xl bg-accent/50 p-4 sm:p-5">
                <h2 className="text-lg font-semibold text-ink">Consent for disclosure and declaration of applicant</h2>
                <p className="text-sm text-ink">{form.declarationText}</p>
                <div className="flex items-start gap-3">
                  <Checkbox id="accept" checked={accepted} onCheckedChange={(v) => setAccepted(v === true)} className="mt-0.5" />
                  <Label htmlFor="accept" className="leading-snug font-normal">
                    I have read the declaration and agree to it. I understand that signing locks this form.
                  </Label>
                </div>
                <T errors={{}} path="signer" label="Type your full name" required autoComplete="name" value={signerName} onChange={setSignerName} placeholder={`${data.personal.firstName} ${data.personal.lastName}`.trim()} />
                <div className="grid gap-1.5">
                  <Label>Draw your signature</Label>
                  <SignaturePad onChange={(v) => { setSignature(v); setSignError(undefined) }} error={signError} />
                </div>
              </section>
            </>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4">
            <Button type="button" variant="outline" disabled={stepIndex === 0 || busy !== null} onClick={() => go(steps[stepIndex - 1].id)}>
              <ArrowLeft /> Back
            </Button>
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="ghost" disabled={busy !== null} onClick={saveDraft}>
                {busy === 'save' ? <Loader2 className="animate-spin" /> : <Save />} Save draft
              </Button>
              {step === 'sign' ? (
                <Button type="button" className="px-5" disabled={busy !== null} onClick={submit}>
                  {busy === 'submit' && <Loader2 className="animate-spin" />} Sign and submit
                </Button>
              ) : (
                <Button type="button" className="px-5" disabled={busy !== null} onClick={() => go(steps[stepIndex + 1].id)}>
                  Next <ArrowRight />
                </Button>
              )}
            </div>
          </div>
        </CardContent>
      </Card>
      <p className="text-center text-xs text-muted-foreground">
        Questions? <Link to="/portal" className="underline">Back to your home page</Link>
      </p>
    </>
  )
}

function Section({ title, hint, error, columns = 'sm:grid-cols-2', children }: { title: string; hint?: string; error?: string; columns?: string; children: React.ReactNode }) {
  return (
    <section className="grid gap-3">
      <div>
        <h2 className="text-lg font-semibold text-ink">{title}</h2>
        {hint && <p className="text-sm text-muted-foreground">{hint}</p>}
        {error && <p className="mt-1 text-sm text-destructive">{error}</p>}
      </div>
      <div className={`grid gap-4 ${columns}`}>{children}</div>
    </section>
  )
}

function Entry({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <fieldset className="rounded-xl border p-4">
      <legend className="px-1 text-sm font-medium text-ink">{title}</legend>
      <div className="grid gap-4 sm:grid-cols-2">{children}</div>
    </fieldset>
  )
}

const fieldNames: Record<string, string> = {
  firstname: 'first name',
  lastname: 'last name',
  gender: 'gender',
  birthdate: 'date of birth',
  citizenshipcountry: 'country of citizenship',
  homeaddress: 'home address',
  city: 'city',
  country: 'country',
  email: 'email',
  phone: 'phone number',
  nextofkinname: 'next of kin name',
  nextofkinemail: 'next of kin email',
  destinationcountry: 'destination country',
  programlevel: 'program level',
  startyear: 'start year',
  startterm: 'start term',
  institutions: 'institutions',
  programchoices: 'program choices',
  highschools: 'high school',
  postsecondary: 'post-secondary institution',
  name: 'name',
  institution: 'institution',
  provincecountry: 'province, country',
  started: 'start date',
  completed: 'completion date',
  grade: 'grade',
}

/** "academic.highschools.0.name" → "High school 1, name". */
function fieldName(path: string): string {
  const parts = path.split('.')
  const last = parts[parts.length - 1]
  const readable = fieldNames[last] ?? last
  const index = parts.findIndex((p) => /^\d+$/.test(p))
  const text = index > 0 ? `${fieldNames[parts[index - 1]] ?? parts[index - 1]} ${Number(parts[index]) + 1}, ${readable}` : readable
  return text.charAt(0).toUpperCase() + text.slice(1)
}
