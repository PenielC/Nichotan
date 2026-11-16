import { useState, type FormEvent } from 'react'
import { PhoneHint } from '@/components/phone-hint'
import { phoneAdvice, phoneProblem } from '@/lib/phone'
import { Briefcase, CheckCircle2, GraduationCap, Loader2, Mail, Phone, Send, Wallet } from 'lucide-react'
import { Field, FormError, SelectField } from '@/components/form'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Label } from '@/components/ui/label'
import { ApiError, post } from '@/lib/api'
import { contact, destinationOptions, qualifications, type DestinationInterest, type Qualification } from '@/lib/enquiries'

const goodToKnow = [
  { icon: Wallet, text: 'Please note all programs are self funded.' },
  { icon: Briefcase, text: 'There are also opportunities for part time working whilst pursuing studies.' },
  { icon: GraduationCap, text: 'There are also opportunities for working after graduation with a possibility of obtaining permanent residency.' },
]

const thisYear = new Date().getFullYear()
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

interface Values {
  name: string
  email: string
  phone: string
  qualification: Qualification | ''
  year: string
  programme: string
  guidance: boolean
  destination: DestinationInterest | ''
  message: string
  website: string
}

const empty: Values = { name: '', email: '', phone: '', qualification: '', year: '', programme: '', guidance: false, destination: '', message: '', website: '' }

function check(v: Values): Record<string, string> {
  const e: Record<string, string> = {}
  if (!v.name.trim()) e.name = 'Tell us your name.'
  if (!v.email.trim() && !v.phone.trim()) e.contact = 'Give an email address or a phone number so we can reach you.'
  if (v.email.trim() && !emailPattern.test(v.email.trim())) e.email = 'Enter a valid email address.'
  if (phoneProblem(v.phone)) e.phone = phoneAdvice
  if (!v.qualification) e.qualification = 'Choose your highest qualification.'
  const year = Number(v.year)
  if (!Number.isInteger(year) || year < 1950 || year > thisYear + 8) e.year = 'Enter the year you graduated, or expect to.'
  if (!v.programme.trim() && !v.guidance) e.programme = 'Name a programme, or tick that you’d like guidance.'
  if (!v.destination) e.destination = 'Let us know what you think of Canada and Germany.'
  return e
}

/** The "Enquire" block of the landing page: what to know, how to reach us, and a short form anyone can send. */
export function EnquirySection() {
  const [values, setValues] = useState<Values>(empty)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const set = <K extends keyof Values>(key: K, value: Values[K]) => setValues((v) => ({ ...v, [key]: value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const problems = check(values)
    setErrors(problems)
    setFormError(null)
    if (Object.keys(problems).length > 0) return setFormError('Please check the highlighted fields.')
    setBusy(true)
    try {
      await post('/api/enquiries', {
        name: values.name.trim(),
        email: values.email.trim() || null,
        phone: values.phone.trim() || null,
        highestQualification: values.qualification,
        graduationYear: Number(values.year),
        preferredProgramme: values.programme.trim() || null,
        openToGuidance: values.guidance,
        destinationInterest: values.destination,
        message: values.message.trim() || null,
        website: values.website,
      })
      setSent(true)
    } catch (error) {
      setFormError(error instanceof ApiError ? (error.status === 429 ? 'You’ve sent a few enquiries already. Please wait a few minutes, or call us.' : error.message) : 'We couldn’t reach Nichotan. Check your connection, or call us on ' + contact.phoneDisplay + '.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section id="enquire" aria-labelledby="enquire-title" className="scroll-mt-4 px-4 py-16 sm:px-6">
      <div className="relative mx-auto max-w-6xl overflow-hidden rounded-3xl bg-ink text-white shadow-xl">
        <div aria-hidden className="absolute -top-32 -right-24 size-96 rounded-full bg-leaf/20 blur-3xl" />
        <div aria-hidden className="absolute -bottom-40 -left-24 size-[28rem] rounded-full bg-[#1d4a8f]/50 blur-3xl" />
        <div className="relative grid gap-10 p-6 sm:p-10 lg:grid-cols-[1fr_1.1fr] lg:gap-14 lg:p-14">
          <div className="flex flex-col">
            <p className="text-sm font-semibold tracking-widest text-leaf uppercase">Enquiries</p>
            <h2 id="enquire-title" className="mt-3 text-3xl leading-tight font-bold sm:text-4xl">
              Thinking about studying <span className="text-leaf">abroad?</span> Ask us.
            </h2>
            <p className="mt-4 max-w-md text-white/80">Tell us a little about where you are now and we’ll come back to you with the next steps for Canada or Germany. No account needed.</p>

            <ul className="mt-8 grid gap-3">
              {goodToKnow.map(({ icon: Icon, text }) => (
                <li key={text} className="flex items-start gap-3 rounded-2xl bg-white/8 p-4 ring-1 ring-white/10 backdrop-blur-sm">
                  <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-leaf/20 text-leaf">
                    <Icon className="size-5" aria-hidden />
                  </span>
                  <span className="pt-1.5 text-sm leading-snug text-white/90">{text}</span>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-xs text-white/60">Opportunities and eligibility depend on each country’s rules and aren’t guaranteed.</p>

            <div className="mt-auto pt-8">
              <p className="text-sm text-white/70">Feel free to call or email us:</p>
              <div className="mt-3 flex flex-wrap gap-3">
                <a href={contact.phoneLink} className="inline-flex items-center gap-2 rounded-full bg-white/10 px-4 py-2.5 text-sm font-medium ring-1 ring-white/20 hover:bg-white/15">
                  <Phone className="size-4 text-leaf" aria-hidden /> {contact.phoneDisplay}
                </a>
                <a href={contact.emailLink} className="inline-flex items-center gap-2 rounded-full bg-white/10 px-4 py-2.5 text-sm font-medium ring-1 ring-white/20 hover:bg-white/15">
                  <Mail className="size-4 text-leaf" aria-hidden /> {contact.email}
                </a>
              </div>
            </div>
          </div>

          <div className="rounded-2xl bg-card p-5 text-foreground shadow-lg sm:p-7">
            {sent ? (
              <div role="status" className="flex h-full flex-col items-center justify-center gap-3 py-10 text-center">
                <span className="flex size-14 items-center justify-center rounded-full bg-accent text-primary">
                  <CheckCircle2 className="size-8" aria-hidden />
                </span>
                <h3 className="text-xl font-bold text-ink">Thank you, we’ve got your enquiry</h3>
                <p className="max-w-sm text-sm text-muted-foreground">A counsellor will be in touch soon. If it’s urgent, call us on {contact.phoneDisplay}.</p>
                <Button
                  variant="outline"
                  onClick={() => {
                    setValues(empty)
                    setSent(false)
                  }}
                >
                  Send another enquiry
                </Button>
              </div>
            ) : (
              <form onSubmit={submit} noValidate className="grid gap-4">
                <div>
                  <h3 className="text-xl font-bold text-ink">Send us an enquiry</h3>
                  <p className="mt-1 text-sm text-muted-foreground">It takes about a minute.</p>
                </div>
                <FormError message={formError} />
                <Field label="Your name" required autoComplete="name" value={values.name} onChange={(e) => set('name', e.target.value)} error={errors.name} maxLength={150} />
                <div className="grid gap-4 sm:grid-cols-2">
                  <Field label="Email" type="email" autoComplete="email" value={values.email} onChange={(e) => set('email', e.target.value)} error={errors.email} maxLength={256} />
                  <Field label="Phone or WhatsApp (include your country code)" type="tel" inputMode="tel" autoComplete="tel" placeholder="+263 77 123 4567" hint={values.phone.trim() ? <PhoneHint value={values.phone} /> : undefined} value={values.phone} onChange={(e) => set('phone', e.target.value)} error={errors.phone} maxLength={30} />
                </div>
                {errors.contact ? <p className="-mt-2 text-xs text-destructive">{errors.contact}</p> : <p className="-mt-2 text-xs text-muted-foreground">Give at least one, so we can reply.</p>}

                <SelectField label="What is your highest level of qualification?" required options={qualifications} value={values.qualification} onChange={(e) => set('qualification', e.target.value as Qualification)} error={errors.qualification} />
                <Field label="The year you graduated (or expect to)" required type="number" inputMode="numeric" min={1950} max={thisYear + 8} placeholder={String(thisYear - 1)} value={values.year} onChange={(e) => set('year', e.target.value)} error={errors.year} />

                <div className="grid gap-2">
                  <Field label="What is your preferred programme of study?" required={!values.guidance} placeholder="e.g. Nursing, Computer Science" value={values.programme} onChange={(e) => set('programme', e.target.value)} error={errors.programme} maxLength={200} />
                  <div className="flex items-center gap-2">
                    <Checkbox id="guidance" checked={values.guidance} onCheckedChange={(v) => set('guidance', v === true)} />
                    <Label htmlFor="guidance" className="font-normal">
                      I’m not sure yet, I’d like guidance
                    </Label>
                  </div>
                </div>

                <SelectField label="Are you open to our recommended study destinations, Canada and Germany?" required options={destinationOptions} value={values.destination} onChange={(e) => set('destination', e.target.value as DestinationInterest)} error={errors.destination} />

                <div className="grid gap-1.5">
                  <Label htmlFor="enquiry-message">Anything else you’d like us to know?</Label>
                  <textarea id="enquiry-message" rows={3} maxLength={2000} value={values.message} onChange={(e) => set('message', e.target.value)} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
                </div>

                {/* A trap for simple bots: people never see it, so it stays empty. */}
                <div aria-hidden className="absolute -left-[9999px] h-0 w-0 overflow-hidden">
                  <label>
                    Website
                    <input tabIndex={-1} autoComplete="off" value={values.website} onChange={(e) => set('website', e.target.value)} />
                  </label>
                </div>

                <Button type="submit" size="lg" className="h-12 text-base" disabled={busy}>
                  {busy ? <Loader2 className="animate-spin" /> : <Send />} Send my enquiry
                </Button>
                <p className="text-center text-xs text-muted-foreground">We use your details only to reply to your enquiry.</p>
              </form>
            )}
          </div>
        </div>
      </div>
    </section>
  )
}
