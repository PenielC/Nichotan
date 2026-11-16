import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Lightbulb, Plus, Send, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { AdmissionStatusBadge } from '@/components/admission-status'
import { Field, FormError, SelectField } from '@/components/form'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, get, post } from '@/lib/api'
import { applicantLevelLabel, applicantLevelOf, applicationKey, destinations, startTerms, type ApplicationFormResult, type DestinationCountry, type StartTerm } from '@/lib/application'
import { candidateStatusLabel, dayText, intakeText, type DirectoryInstitution, type MyAdmission, type Suggestion } from '@/lib/admissions'
import { UploadDialog } from '@/components/document-parts'
import { download } from '@/lib/api'
import { MyPreDeparture } from '@/components/my-pre-departure'
import { MyVisas } from '@/components/my-visas'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const mineKey = ['admissions', 'mine'] as const

export default function Applications() {
  const queryClient = useQueryClient()
  // Staff change these (offers, acceptances), so always fetch the latest when the page opens.
  const list = useQuery({ queryKey: mineKey, queryFn: () => get<MyAdmission[]>('/api/admissions/mine'), staleTime: 0 })
  const suggestions = useQuery({ queryKey: ['admissions', 'suggestions', 'mine'], queryFn: () => get<Suggestion[]>('/api/admissions/suggestions/mine') })
  const [adding, setAdding] = useState(false)
  const [suggesting, setSuggesting] = useState(false)
  const [withdrawing, setWithdrawing] = useState<MyAdmission | null>(null)
  const [offerFor, setOfferFor] = useState<MyAdmission | null>(null)

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['admissions'] })
  const send = useMutation({
    mutationFn: (id: string) => post<MyAdmission>(`/api/admissions/mine/${id}/send`),
    onSuccess: () => {
      toast.success('Sent to your counsellor. They’ll review it and apply for you.')
      refresh()
    },
    onError: (e) => toast.error(message(e)),
  })

  const open = list.data?.filter((a) => a.status !== 'Withdrawn' && a.status !== 'Closed' && a.status !== 'Rejected').length ?? 0

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-ink">My applications</h1>
          <p className="mt-1 max-w-2xl text-muted-foreground">Choose the schools and programmes you want. When one is ready, send it to your counsellor, who checks it and applies on the school’s website for you.</p>
        </div>
        <Button onClick={() => setAdding(true)} disabled={open >= 5}>
          <Plus /> Add an application
        </Button>
      </div>
      <MyVisas />
      <MyPreDeparture />
      {open >= 5 && <p className="-mt-2 text-sm text-muted-foreground">You have 5 open applications, the most you can have at once. Withdraw one to add another.</p>}

      {list.isError ? (
        <FormError message="We couldn’t load your applications. Please refresh the page." />
      ) : !list.data ? (
        <Skeleton className="h-48" />
      ) : list.data.length === 0 ? (
        <Card>
          <CardContent className="py-10 text-center">
            <p className="font-medium text-ink">You haven’t added an application yet</p>
            <p className="mx-auto mt-1 max-w-md text-sm text-muted-foreground">Pick a school and programme from our list to get started. Your counsellor can help if you’re not sure where to apply.</p>
            <Button className="mt-4" onClick={() => setAdding(true)}>
              <Plus /> Add an application
            </Button>
          </CardContent>
        </Card>
      ) : (
        list.data.map((a) => (
          <Card key={a.id} className={a.status === 'Withdrawn' || a.status === 'Closed' ? 'opacity-70' : undefined}>
            <CardHeader className="flex-row flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <CardTitle className="text-lg text-ink">{a.institutionName}</CardTitle>
                <CardDescription>
                  {a.programmeName} · {a.country} · {intakeText(a.intakeTerm, a.intakeYear)}
                </CardDescription>
              </div>
              <AdmissionStatusBadge status={a.status} label={candidateStatusLabel[a.status]} />
            </CardHeader>
            <CardContent className="grid gap-4">
              {a.blockers.length > 0 && (
                <Alert>
                  <AlertTitle>To send this to your counsellor, you still need to:</AlertTitle>
                  <AlertDescription>
                    <ul className="mt-1 grid list-disc gap-0.5 pl-5">
                      {a.blockers.map((b) => (
                        <li key={b}>
                          {b}{' '}
                          <Link to={b.includes('form') ? '/portal/application' : '/portal/documents'} className="underline underline-offset-2">
                            Go there
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </AlertDescription>
                </Alert>
              )}
              <Progress application={a} onUploadOffer={() => setOfferFor(a)} />
              {a.status === 'ReadyForReview' && <p className="text-sm text-muted-foreground">Your counsellor is checking this. You’ll see it change here when they’ve applied to the school.</p>}
              {a.submittedToInstitutionOn && <p className="text-sm text-muted-foreground">Your counsellor applied to the school on {dayText(a.submittedToInstitutionOn)}.</p>}
              {(a.canSend || a.canWithdraw) && (
                <div className="flex flex-wrap gap-2">
                  {a.canSend && (
                    <Button onClick={() => send.mutate(a.id)} disabled={send.isPending}>
                      <Send /> Send to my counsellor
                    </Button>
                  )}
                  {a.canWithdraw && (
                    <Button variant="outline" onClick={() => setWithdrawing(a)}>
                      Withdraw
                    </Button>
                  )}
                </div>
              )}
            </CardContent>
          </Card>
        ))
      )}

      {suggestions.data && suggestions.data.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-lg text-ink">Schools you’ve suggested</CardTitle>
            <CardDescription>We’ll add them to the list if we can, and let you know.</CardDescription>
          </CardHeader>
          <CardContent className="grid gap-2">
            {suggestions.data.map((s) => (
              <div key={s.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm">
                <span>
                  <span className="font-medium text-ink">{s.institutionName}</span>
                  <span className="block text-xs text-muted-foreground">
                    {s.programmeName} · {s.country}
                  </span>
                </span>
                <Badge variant={s.status === 'Added' ? 'default' : s.status === 'Declined' ? 'outline' : 'secondary'}>{s.status === 'New' ? 'Waiting for us' : s.status === 'Added' ? 'Added to the list' : 'Not available'}</Badge>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      {adding && <AddDialog onClose={() => setAdding(false)} onSuggest={() => { setAdding(false); setSuggesting(true) }} onDone={refresh} />}
      {suggesting && <SuggestDialog onClose={() => setSuggesting(false)} onDone={refresh} />}
      {offerFor && <OfferLetterDialog application={offerFor} onClose={() => setOfferFor(null)} onDone={refresh} />}
      {withdrawing && <WithdrawDialog application={withdrawing} onClose={() => setWithdrawing(null)} onDone={refresh} />}
    </div>
  )
}

function AddDialog({ onClose, onSuggest, onDone }: { onClose: () => void; onSuggest: () => void; onDone: () => void }) {
  const form = useQuery({ queryKey: applicationKey, queryFn: () => get<ApplicationFormResult>('/api/application-form') })
  const directory = useQuery({ queryKey: ['admissions', 'directory'], queryFn: () => get<DirectoryInstitution[]>('/api/admissions/directory') })
  const [country, setCountry] = useState<DestinationCountry | ''>('')
  const [institutionId, setInstitutionId] = useState('')
  const [programmeId, setProgrammeId] = useState('')
  const [intake, setIntake] = useState('')

  // Programmes at the level on their own form, so they aren't offered the wrong kind of study.
  const level = applicantLevelOf(form.data?.data.program.programLevel ?? null)
  const destination = form.data?.data.program.destinationCountry ?? null
  const institutions = (directory.data ?? []).filter((i) => (!country || i.country === country) && i.programmes.some((p) => !level || p.level === level))
  const institution = institutions.find((i) => i.id === institutionId)
  const programmes = (institution?.programmes ?? []).filter((p) => !level || p.level === level)
  const programme = programmes.find((p) => p.id === programmeId)

  const create = useMutation({
    mutationFn: () => {
      const [term, year] = intake.split('|')
      return post<MyAdmission>('/api/admissions/mine', { programmeId, intakeTerm: term as StartTerm, intakeYear: Number(year) })
    },
    onSuccess: () => {
      toast.success('Application added.')
      onDone()
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(o) => !o && !create.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add an application</DialogTitle>
          <DialogDescription>
            {level ? `Showing ${applicantLevelLabel[level].toLowerCase()} programmes, matching your application form.` : 'Choose a school and programme. Fill in your application form to narrow this to your level of study.'}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <SelectField
            label="Country"
            options={destinations.map((c) => ({ value: c, label: c }))}
            placeholder={destination ? `All (you chose ${destination})` : 'All countries'}
            value={country}
            onChange={(e) => {
              setCountry(e.target.value as DestinationCountry)
              setInstitutionId('')
              setProgrammeId('')
              setIntake('')
            }}
          />
          <SelectField
            label="School"
            required
            options={institutions.map((i) => ({ value: i.id, label: `${i.name} (${i.country})` }))}
            value={institutionId}
            onChange={(e) => {
              setInstitutionId(e.target.value)
              setProgrammeId('')
              setIntake('')
            }}
          />
          <SelectField
            label="Programme"
            required
            disabled={!institution}
            options={programmes.map((p) => ({ value: p.id, label: p.name }))}
            value={programmeId}
            onChange={(e) => {
              setProgrammeId(e.target.value)
              setIntake('')
            }}
          />
          <SelectField
            label="When do you want to start?"
            required
            disabled={!programme}
            options={(programme?.intakes ?? []).map((i) => ({ value: `${i.term}|${i.year}`, label: intakeText(i.term, i.year) }))}
            value={intake}
            onChange={(e) => setIntake(e.target.value)}
          />
          <button type="button" onClick={onSuggest} className="inline-flex items-center gap-1.5 justify-self-start text-sm text-primary underline-offset-2 hover:underline">
            <Lightbulb className="size-4" aria-hidden /> Can’t find your school? Suggest it
          </button>
          <FormError message={create.error ? message(create.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>
            Cancel
          </Button>
          <Button onClick={() => create.mutate()} disabled={!intake || create.isPending}>
            Add application
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function SuggestDialog({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const [school, setSchool] = useState('')
  const [country, setCountry] = useState<DestinationCountry | ''>('')
  const [programme, setProgramme] = useState('')
  const [term, setTerm] = useState<StartTerm | ''>('')
  const [year, setYear] = useState('')
  const [note, setNote] = useState('')
  const send = useMutation({
    mutationFn: () => post<Suggestion>('/api/admissions/suggestions', { institutionName: school.trim(), country, programmeName: programme.trim(), intakeTerm: term || null, intakeYear: year ? Number(year) : null, note: note.trim() || null }),
    onSuccess: () => {
      toast.success('Thank you. We’ll look into it and let you know.')
      onDone()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !send.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Suggest a school</DialogTitle>
          <DialogDescription>Tell us where you’d like to apply. We’ll add it to the list if we can and you can then add the application yourself.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-4">
          <Field label="School" required value={school} onChange={(e) => setSchool(e.target.value)} maxLength={200} />
          <SelectField label="Country" required options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value as DestinationCountry)} />
          <Field label="Programme" required value={programme} onChange={(e) => setProgramme(e.target.value)} maxLength={200} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Start (optional)" options={startTerms} value={term} onChange={(e) => setTerm(e.target.value as StartTerm)} />
            <Field label="Year (optional)" type="number" inputMode="numeric" value={year} onChange={(e) => setYear(e.target.value)} />
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor="suggest-note">Anything else?</Label>
            <textarea id="suggest-note" value={note} onChange={(e) => setNote(e.target.value)} rows={2} maxLength={1000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
          </div>
          <FormError message={send.error ? message(send.error) : null} />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={send.isPending}>
            Cancel
          </Button>
          <Button onClick={() => send.mutate()} disabled={!school.trim() || !country || !programme.trim() || send.isPending}>
            Send suggestion
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function WithdrawDialog({ application, onClose, onDone }: { application: MyAdmission; onClose: () => void; onDone: () => void }) {
  const withdraw = useMutation({
    mutationFn: () => post<MyAdmission>(`/api/admissions/mine/${application.id}/withdraw`),
    onSuccess: () => {
      toast.success('Application withdrawn.')
      onDone()
      onClose()
    },
  })
  return (
    <Dialog open onOpenChange={(o) => !o && !withdraw.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Withdraw this application?</DialogTitle>
          <DialogDescription>
            {application.institutionName}, {application.programmeName} ({intakeText(application.intakeTerm, application.intakeYear)}). You can add it again later.
          </DialogDescription>
        </DialogHeader>
        <FormError message={withdraw.error ? message(withdraw.error) : null} />
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={withdraw.isPending}>
            Keep it
          </Button>
          <Button variant="destructive" onClick={() => withdraw.mutate()} disabled={withdraw.isPending}>
            Withdraw
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}


async function save(documentId: string, name: string) {
  try {
    await download(`/api/documents/${documentId}/file`, name)
  } catch {
    toast.error('We couldn’t download the file. Please try again.')
  }
}

/** The offer and the formal acceptance, once the school has replied. */
function Progress({ application: a, onUploadOffer }: { application: MyAdmission; onUploadOffer: () => void }) {
  if (!a.offer && !a.acceptance && !a.canUploadOfferLetter && !a.offerLetter) return null
  return (
    <div className="grid gap-3 rounded-xl bg-accent/40 p-4 text-sm">
      {(a.canUploadOfferLetter || a.offerLetter) && (
        <div className="grid gap-1">
          <p className="font-semibold text-ink">{a.offerLetter ? 'Your offer letter' : 'Heard back from the school?'}</p>
          {!a.offerLetter && <p className="text-muted-foreground">If the school has sent you an offer, upload the letter here so your counsellor can record it.</p>}
          {a.offerLetter && (
            <button type="button" className="inline-flex items-center gap-1 justify-self-start text-primary hover:underline" onClick={() => void save(a.offerLetter!.documentId, a.offerLetter!.fileName)}>
              <Download className="size-3.5" /> {a.offerLetter.fileName}
            </button>
          )}
          {a.canUploadOfferLetter && a.offerLetterTypeId && (
            <Button size="sm" variant={a.offerLetter ? 'outline' : 'default'} className="justify-self-start" onClick={onUploadOffer}>
              <Upload /> {a.offerLetter ? 'Replace the letter' : 'Upload your offer letter'}
            </Button>
          )}
        </div>
      )}
      {a.offer && (
        <div className="grid gap-1">
          <p className="font-semibold text-ink">You have an offer</p>
          {a.offer.conditions && <p className="text-muted-foreground">Conditions: {a.offer.conditions}</p>}
          {a.offer.respondByOn && <p className="text-muted-foreground">The school would like a reply by {dayText(a.offer.respondByOn)}.</p>}
        </div>
      )}
      {a.acceptance && (
        <div className="grid gap-1">
          <p className="font-semibold text-ink">Formal acceptance received on {dayText(a.acceptance.receivedOn)}</p>
          {a.acceptance.palNumber && <p className="text-muted-foreground">Your PAL number: <span className="font-medium text-ink">{a.acceptance.palNumber}</span></p>}
          <div className="flex flex-wrap gap-3">
            {a.acceptance.letterDocumentId && (
              <button type="button" className="inline-flex items-center gap-1 text-primary hover:underline" onClick={() => void save(a.acceptance!.letterDocumentId!, 'acceptance-letter')}>
                <Download className="size-3.5" /> Acceptance letter
              </button>
            )}
            {a.acceptance.palLetterDocumentId && (
              <button type="button" className="inline-flex items-center gap-1 text-primary hover:underline" onClick={() => void save(a.acceptance!.palLetterDocumentId!, 'pal-letter')}>
                <Download className="size-3.5" /> PAL letter
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function OfferLetterDialog({ application, onClose, onDone }: { application: MyAdmission; onClose: () => void; onDone: () => void }) {
  if (!application.offerLetterTypeId) return null
  return (
    <UploadDialog
      slot={{ documentTypeId: application.offerLetterTypeId, typeName: 'offer letter', description: 'The letter the school sent you.', applicationId: application.id, applicationLabel: application.institutionName, tracksExpiry: false, version: application.offerLetter ? 1 : 0 }}
      onClose={onClose}
      onDone={onDone}
    />
  )
}
