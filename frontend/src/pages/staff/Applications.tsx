import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, Download, Loader2, RotateCcw, Search } from 'lucide-react'
import { toast } from 'sonner'
import { FormStatusBadge, FormSummary } from '@/components/application-summary'
import { FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, download, get, post, query, type Paged } from '@/lib/api'
import { applicantLevelLabel, statusLabel, type ApplicationFormResult, type ApplicationFormSummary, type FormStatus } from '@/lib/application'
import { formatDate, formatDateTime } from '@/lib/format'

const pageSize = 25
const statuses = (Object.keys(statusLabel) as FormStatus[]).map((value) => ({ value, label: statusLabel[value] }))

export function ApplicationList() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [text, setText] = useState(search)

  const setFilters = (next: { search?: string; status?: string; page?: number }) => {
    const merged = { search, status, page: 1, ...next }
    setParams(Object.fromEntries(Object.entries(merged).filter(([k, v]) => v && !(k === 'page' && v === 1)).map(([k, v]) => [k, String(v)])), { replace: true })
  }

  useEffect(() => {
    const timer = setTimeout(() => {
      if (text.trim() !== search) setFilters({ search: text.trim() })
    }, 300)
    return () => clearTimeout(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text])

  const list = useQuery({
    queryKey: ['application-forms', 'list', search, status, page],
    queryFn: () => get<Paged<ApplicationFormSummary>>(`/api/application-forms${query({ search, status, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / pageSize)) : 1

  return (
    <>
      <PageHeader title="Application forms" description="Nichotan application forms that candidates have started or submitted." />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="relative min-w-60 flex-1 sm:max-w-md">
              <span className="sr-only">Search forms</span>
              <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <Input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search by name or email" className="h-10 pl-9" maxLength={100} />
            </label>
            <SelectField label="Status" className="w-44" placeholder="All statuses" options={statuses} value={status} onChange={(e) => setFilters({ status: e.target.value })} />
          </div>

          {list.isError ? (
            <FormError message="We couldn’t load the forms. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-64" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search || status ? 'No forms match these filters.' : 'No one has started a form yet.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Candidate</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="hidden md:table-cell">Destination</TableHead>
                  <TableHead className="hidden md:table-cell">Level</TableHead>
                  <TableHead className="text-right">Last changed</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((f) => (
                  <TableRow key={f.candidateId} className="cursor-pointer" onClick={() => navigate(`/staff/candidates/${f.candidateId}`)}>
                    <TableCell>
                      <Link to={`/staff/candidates/${f.candidateId}`} onClick={(e) => e.stopPropagation()} className="font-medium text-ink hover:underline">
                        {f.candidateName}
                      </Link>
                      <div className="text-xs text-muted-foreground">{f.email}</div>
                    </TableCell>
                    <TableCell>
                      <FormStatusBadge status={f.status} />
                      {f.currentVersion > 0 && <span className="ml-2 text-xs text-muted-foreground">v{f.currentVersion}</span>}
                    </TableCell>
                    <TableCell className="hidden md:table-cell">{f.destinationCountry ?? '—'}</TableCell>
                    <TableCell className="hidden md:table-cell">{f.applicantLevel ? applicantLevelLabel[f.applicantLevel] : '—'}</TableCell>
                    <TableCell className="text-right text-muted-foreground">{formatDate(f.lastChangedAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {list.data && list.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {list.data.total} forms
              </span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setFilters({ page: page - 1 })}>
                  <ChevronLeft /> Previous
                </Button>
                <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setFilters({ page: page + 1 })}>
                  Next <ChevronRight />
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </>
  )
}

/** A candidate's application form on their record: status, signed versions, and sending it back for corrections. */
export function ApplicationFormCard({ candidateId }: { candidateId: string }) {
  // Opening a form is audited on the server, so it isn't refetched on window focus.
  const form = useQuery({
    queryKey: ['application-forms', candidateId],
    queryFn: () => get<ApplicationFormResult>(`/api/application-forms/${candidateId}`),
    refetchOnWindowFocus: false,
    retry: false,
  })
  const [reopening, setReopening] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (form.isError) {
    const notStarted = form.error instanceof ApiError && form.error.status === 404
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-ink">Application form</CardTitle>
          <CardDescription>{notStarted ? 'This candidate hasn’t started their application form yet.' : 'We couldn’t load the form.'}</CardDescription>
        </CardHeader>
      </Card>
    )
  }
  if (!form.data) return <Skeleton className="h-48" />
  const f = form.data

  const pdf = async (n: number) => {
    setError(null)
    try {
      await download(`/api/application-forms/${candidateId}/versions/${n}/pdf`, `Nichotan-application-form-v${n}.pdf`)
    } catch {
      setError('We couldn’t download the PDF. Please try again.')
    }
  }

  return (
    <>
      <Card>
        <CardHeader className="flex-row flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="text-ink">Application form</CardTitle>
            <CardDescription>
              {f.applicantLevel ? `${applicantLevelLabel[f.applicantLevel]} applicant · ` : ''}
              {f.submittedAtUtc ? `submitted ${formatDateTime(f.submittedAtUtc)}` : 'not submitted yet'}
            </CardDescription>
          </div>
          <div className="flex items-center gap-2">
            <FormStatusBadge status={f.status} />
            {f.status === 'Submitted' && (
              <Button variant="outline" size="sm" onClick={() => setReopening(true)}>
                <RotateCcw /> Reopen for corrections
              </Button>
            )}
          </div>
        </CardHeader>
        <CardContent className="grid gap-6">
          <FormError message={error} />
          {f.status === 'Reopened' && (
            <Alert>
              <AlertTitle>Waiting for the candidate</AlertTitle>
              <AlertDescription>
                Sent back {f.reopenedAtUtc ? formatDateTime(f.reopenedAtUtc) : ''}: {f.reopenReason}
              </AlertDescription>
            </Alert>
          )}
          {f.versions.length > 0 && (
            <div className="grid gap-2">
              <h3 className="text-sm font-semibold text-ink">Signed versions</h3>
              {[...f.versions].reverse().map((v) => (
                <div key={v.versionNumber} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border px-3 py-2 text-sm">
                  <span>
                    <span className="font-medium text-ink">Version {v.versionNumber}</span>
                    <span className="text-muted-foreground">
                      {' '}
                      · signed by {v.signerName} · {formatDateTime(v.submittedAtUtc)}
                    </span>
                  </span>
                  <Button variant="outline" size="sm" onClick={() => pdf(v.versionNumber)}>
                    <Download /> PDF
                  </Button>
                </div>
              ))}
            </div>
          )}
          <div>
            <h3 className="mb-3 text-sm font-semibold text-ink">{f.status === 'Submitted' ? 'Current answers' : 'Answers so far (draft)'}</h3>
            <FormSummary data={f.data} />
          </div>
        </CardContent>
      </Card>
      <ReopenDialog candidateId={candidateId} open={reopening} onClose={() => setReopening(false)} />
    </>
  )
}

function ReopenDialog({ candidateId, open, onClose }: { candidateId: string; open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [reason, setReason] = useState('')
  const reopen = useMutation({
    mutationFn: () => post<ApplicationFormResult>(`/api/application-forms/${candidateId}/reopen`, { reason: reason.trim() }),
    onSuccess: (result) => {
      queryClient.setQueryData(['application-forms', candidateId], result)
      void queryClient.invalidateQueries({ queryKey: ['application-forms', 'list'] })
      toast.success('The form has been sent back to the candidate.')
      setReason('')
      onClose()
    },
  })

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !reopen.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reopen this form for corrections?</DialogTitle>
          <DialogDescription>The candidate is emailed a link, sees your note, edits their answers and signs again. The version they signed stays on record.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor="reopen-reason">What needs correcting?</Label>
          <textarea
            id="reopen-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={1000}
            rows={4}
            className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
            placeholder="e.g. The spelling of your last name doesn’t match your passport."
          />
          <p className="text-xs text-muted-foreground">The candidate sees this note when they sign in. It isn’t put in the email.</p>
        </div>
        <FormError message={reopen.error ? (reopen.error instanceof ApiError ? reopen.error.message : 'Something went wrong. Please try again.') : null} />
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={reopen.isPending}>
            Cancel
          </Button>
          <Button onClick={() => reopen.mutate()} disabled={!reason.trim() || reopen.isPending}>
            {reopen.isPending && <Loader2 className="animate-spin" />} Reopen form
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
