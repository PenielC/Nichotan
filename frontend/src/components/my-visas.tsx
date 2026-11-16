import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Upload } from 'lucide-react'
import { toast } from 'sonner'
import { Field, FormError } from '@/components/form'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { UploadDialog } from '@/components/document-parts'
import { Button } from '@/components/ui/button'
import { VisaStatusBadge } from '@/components/visa-status'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ApiError, download, get, post } from '@/lib/api'
import { dayText, todayIso } from '@/lib/admissions'
import { visaStatusLabel, type MyVisa } from '@/lib/visa'

/** The student's own visa progress, as recorded by their counsellor. Shown only once a visa case exists. */
export function MyVisas() {
  const queryClient = useQueryClient()
  const [uploadFor, setUploadFor] = useState<MyVisa | null>(null)
  const [doneFor, setDoneFor] = useState<MyVisa | null>(null)
  const visas = useQuery({ queryKey: ['visa', 'mine'], queryFn: () => get<MyVisa[]>('/api/visa-cases/mine'), staleTime: 0 })
  if (!visas.data?.length) return null
  const save = (id: string, name: string) => void download(`/api/documents/${id}/file`, name)
  return (
    <>
      {visas.data.map((v) => (
        <Card key={v.id}>
          <CardHeader>
            <CardTitle className="flex flex-wrap items-center gap-2 text-lg text-ink">
              Your {v.country} visa <VisaStatusBadge status={v.status} />
            </CardTitle>
            <CardDescription>
              For {v.programmeName} at {v.institutionName}. Your counsellor updates this from what the immigration authority tells them.
            </CardDescription>
          </CardHeader>
          <CardContent className="grid gap-4 text-sm">
            {v.biometricsAppointmentOn && v.status === 'BiometricsRequested' && (
              <p className="rounded-lg border border-primary/30 bg-accent/40 p-3 text-ink">
                <span className="font-medium">Biometrics appointment: {dayText(v.biometricsAppointmentOn)}</span>
                {v.biometricsLocation ? ` at ${v.biometricsLocation}` : ''}. Bring your passport and the appointment letter.
              </p>
            )}
            {v.canMarkBiometricsDone && (
              <div className="grid gap-2 rounded-lg border border-primary/30 bg-accent/40 p-3 text-ink">
                <p className="font-medium">Next: your biometrics</p>
                <p>Your visa application has been submitted. When you’re asked to give your biometrics, attend your appointment. Then tell us here that it’s done.</p>
                <Button size="sm" className="justify-self-start" onClick={() => setDoneFor(v)}>
                  I’ve done my biometrics
                </Button>
              </div>
            )}
            {v.documentsMissing > 0 && (
              <p className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-amber-900">
                <span className="font-medium">{v.documentsMissing} visa document{v.documentsMissing === 1 ? '' : 's'} still needed.</span>{' '}
                <Link to="/portal/documents" className="underline underline-offset-2">
                  Upload them on your Documents page
                </Link>
                .
              </p>
            )}
            {(v.canUploadBiometricsLetter || v.biometricsLetter || v.decisionLetter) && (
              <div className="flex flex-wrap items-center gap-3">
                {v.canUploadBiometricsLetter && v.biometricsLetterTypeId && (
                  <Button size="sm" variant="outline" onClick={() => setUploadFor(v)}>
                    <Upload /> {v.biometricsLetter ? 'Replace your biometrics letter' : 'Upload your biometrics letter'}
                  </Button>
                )}
                {v.biometricsLetter && (
                  <button type="button" className="inline-flex items-center gap-1 text-primary hover:underline" onClick={() => save(v.biometricsLetter!.documentId, v.biometricsLetter!.fileName)}>
                    <Download className="size-3.5" /> Biometrics letter
                  </button>
                )}
                {v.decisionLetter && (
                  <button type="button" className="inline-flex items-center gap-1 text-primary hover:underline" onClick={() => save(v.decisionLetter!.documentId, v.decisionLetter!.fileName)}>
                    <Download className="size-3.5" /> Decision letter
                  </button>
                )}
              </div>
            )}
            <ol className="grid gap-2">
              {v.history.map((h, i) => (
                <li key={i} className="flex justify-between gap-2 border-l-2 border-primary/30 pl-3">
                  <span className="text-ink">{h.status === 'Preparing' ? 'Visa case opened' : visaStatusLabel[h.status]}</span>
                  <span className="text-muted-foreground">{dayText(h.occurredOn)}</span>
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>
      ))}
      {uploadFor?.biometricsLetterTypeId && (
        <UploadDialog
          slot={{ documentTypeId: uploadFor.biometricsLetterTypeId, typeName: 'biometrics letter', description: 'The letter you were sent asking for your biometrics.', applicationId: uploadFor.applicationId, applicationLabel: uploadFor.institutionName, tracksExpiry: false, version: uploadFor.biometricsLetter ? 1 : 0 }}
          onClose={() => setUploadFor(null)}
          onDone={() => void queryClient.invalidateQueries({ queryKey: ['visa', 'mine'] })}
        />
      )}
      {doneFor && <BiometricsDoneDialog visa={doneFor} onClose={() => setDoneFor(null)} onDone={() => void queryClient.invalidateQueries({ queryKey: ['visa', 'mine'] })} />}
    </>
  )
}

function BiometricsDoneDialog({ visa, onClose, onDone }: { visa: MyVisa; onClose: () => void; onDone: () => void }) {
  const [day, setDay] = useState(todayIso)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const submit = async () => {
    setSaving(true)
    setError(null)
    try {
      await post(`/api/visa-cases/mine/${visa.id}/biometrics-done`, { doneOn: day || null })
      toast.success('Thanks. Your counsellor has been told.')
      onDone()
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
    } finally {
      setSaving(false)
    }
  }
  return (
    <Dialog open onOpenChange={(o) => !o && !saving && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Biometrics done?</DialogTitle>
          <DialogDescription>Only say yes once you’ve given your biometrics. Your counsellor is told and will check.</DialogDescription>
        </DialogHeader>
        <Field label="The day you did them" type="date" max={todayIso()} value={day} onChange={(e) => setDay(e.target.value)} />
        <FormError message={error} />
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>
            Cancel
          </Button>
          <Button onClick={() => void submit()} disabled={saving || !day}>
            Yes, they’re done
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
