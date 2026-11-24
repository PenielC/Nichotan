import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Plus, Send, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { AdmissionStatusBadge } from '@/components/admission-status'
import { FormError } from '@/components/form'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { ApiError, get, post } from '@/lib/api'
import { candidateStatusLabel, dayText, intakeText, type MyAdmission } from '@/lib/admissions'
import { UploadDialog } from '@/components/document-parts'
import { download } from '@/lib/api'
import { MyPreDeparture } from '@/components/my-pre-departure'
import { MyVisas } from '@/components/my-visas'

const message = (e: unknown) => (e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
const mineKey = ['admissions', 'mine'] as const

export default function Applications() {
  const queryClient = useQueryClient()
  // Staff change these (adding an application, offers, acceptances), so always fetch the latest when the page opens.
  const list = useQuery({ queryKey: mineKey, queryFn: () => get<MyAdmission[]>('/api/admissions/mine'), staleTime: 0 })
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

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-ink">My applications</h1>
          <p className="mt-1 max-w-2xl text-muted-foreground">
            Your applications start with your application form: choose your country, program and start date there. Your counsellor then adds your applications here. When one is ready, send it to them and they apply on the school’s website for you.
          </p>
        </div>
        <Button asChild>
          <Link to="/portal/application">
            <Plus /> Add an application
          </Link>
        </Button>
      </div>
      <MyVisas />
      <MyPreDeparture />
      {list.isError ? (
        <FormError message="We couldn’t load your applications. Please refresh the page." />
      ) : !list.data ? (
        <Skeleton className="h-48" />
      ) : list.data.length === 0 ? (
        <Card>
          <CardContent className="py-10 text-center">
            <p className="font-medium text-ink">You don’t have any applications yet</p>
            <p className="mx-auto mt-1 max-w-md text-sm text-muted-foreground">Fill in and sign your application form first. It holds the country, program and start date you want. Your counsellor then chooses schools with you and adds your applications here.</p>
            <Button asChild className="mt-4">
              <Link to="/portal/application">Go to my form</Link>
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

      {offerFor && <OfferLetterDialog application={offerFor} onClose={() => setOfferFor(null)} onDone={refresh} />}
      {withdrawing && <WithdrawDialog application={withdrawing} onClose={() => setWithdrawing(null)} onDone={refresh} />}
    </div>
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
            {application.institutionName}, {application.programmeName} ({intakeText(application.intakeTerm, application.intakeYear)}). You can ask your counsellor to add it again later.
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
