import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, Eye, Mail, Phone, Search } from 'lucide-react'
import { toast } from 'sonner'
import { FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ApiError, get, post, put, query, type Paged } from '@/lib/api'
import { admissionsRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { destinationLabel, enquiryStatusLabel, qualificationLabel, type Enquiry, type EnquiryStatus } from '@/lib/enquiries'
import { formatDate, formatDateTime } from '@/lib/format'

const pageSize = 25
const statuses = (Object.keys(enquiryStatusLabel) as EnquiryStatus[]).map((value) => ({ value, label: enquiryStatusLabel[value] }))
/** "Not yet viewed", or who opened it first and when. */
export function ViewedBadge({ enquiry }: { enquiry: Enquiry }) {
  if (!enquiry.viewedAtUtc) {
    return (
      <Badge variant="destructive" className="gap-1">
        <span aria-hidden className="size-1.5 rounded-full bg-current" /> Not yet viewed
      </Badge>
    )
  }
  return (
    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground" title={`Viewed ${formatDateTime(enquiry.viewedAtUtc)}${enquiry.viewedBy ? ` by ${enquiry.viewedBy}` : ''}`}>
      <Eye className="size-3.5" aria-hidden /> Viewed{enquiry.viewedBy ? ` by ${enquiry.viewedBy.split('@')[0]}` : ''}
    </span>
  )
}

const tone = (s: EnquiryStatus) => (s === 'New' ? 'default' : s === 'Contacted' ? 'secondary' : 'outline')

export function EnquiryList() {
  const { state } = useAuth()
  const canManage = state.status === 'signedIn' && hasAnyRole(state.session, admissionsRoles)
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const status = params.get('status') ?? ''
  const unviewedOnly = params.get('unviewed') === '1'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [text, setText] = useState(search)
  const [open, setOpen] = useState<Enquiry | null>(null)

  const setFilters = (next: Record<string, string | number>) => {
    const merged: Record<string, string | number> = { search, status, unviewed: unviewedOnly ? '1' : '', page: 1, ...next }
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
    queryKey: ['enquiries', 'list', search, status, unviewedOnly, page],
    queryFn: () => get<Paged<Enquiry>>(`/api/enquiries${query({ search, status, unviewedOnly: unviewedOnly ? 'true' : undefined, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / pageSize)) : 1
  const queryClient = useQueryClient()

  /** Opening an enquiry as an administrator or counsellor records that it has been viewed. */
  const open_ = (enquiry: Enquiry) => {
    setOpen(enquiry)
    if (canManage && !enquiry.viewedAtUtc) {
      post<Enquiry>(`/api/enquiries/${enquiry.id}/viewed`)
        .then((updated) => {
          setOpen((current) => (current?.id === updated.id ? updated : current))
          void queryClient.invalidateQueries({ queryKey: ['enquiries'] })
        })
        .catch(() => {})
    }
  }

  return (
    <>
      <PageHeader title="Enquiries" description="People who asked about studying abroad through the website. They don’t have an account yet." />
      <Card>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="relative min-w-60 flex-1 sm:max-w-md">
              <span className="sr-only">Search enquiries</span>
              <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <Input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search by name, email or phone" className="h-10 pl-9" maxLength={100} />
            </label>
            <SelectField label="Status" className="w-44" placeholder="All" options={statuses} value={status} onChange={(e) => setFilters({ status: e.target.value })} />
            <div className="flex items-center gap-2 pb-2.5">
              <Checkbox id="unviewed" checked={unviewedOnly} onCheckedChange={(v) => setFilters({ unviewed: v === true ? '1' : '' })} />
              <Label htmlFor="unviewed" className="font-normal">
                Not yet viewed
              </Label>
            </div>
          </div>

          {list.isError ? (
            <FormError message="We couldn’t load enquiries. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-64" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search || status ? 'No enquiries match.' : 'No enquiries yet.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Who</TableHead>
                  <TableHead className="hidden md:table-cell">Studied</TableHead>
                  <TableHead className="hidden lg:table-cell">Wants</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Reviewed</TableHead>
                  <TableHead className="text-right">Received</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((e) => (
                  <TableRow key={e.id} className={`cursor-pointer ${e.viewedAtUtc ? '' : 'bg-accent/40'}`} onClick={() => open_(e)}>
                    <TableCell>
                      <button type="button" className="text-left font-medium text-ink hover:underline" onClick={() => open_(e)}>
                        {e.name}
                      </button>
                      <div className="text-xs text-muted-foreground">{e.email ?? e.phone}</div>
                    </TableCell>
                    <TableCell className="hidden md:table-cell">
                      {qualificationLabel(e.highestQualification)}
                      <div className="text-xs text-muted-foreground">{e.graduationYear}</div>
                    </TableCell>
                    <TableCell className="hidden lg:table-cell">
                      {e.preferredProgramme ?? 'Wants guidance'}
                      <div className="text-xs text-muted-foreground">{destinationLabel(e.destinationInterest)}</div>
                    </TableCell>
                    <TableCell>
                      <Badge variant={tone(e.status)}>{enquiryStatusLabel[e.status]}</Badge>
                    </TableCell>
                    <TableCell>
                      <ViewedBadge enquiry={e} />
                    </TableCell>
                    <TableCell className="text-right text-muted-foreground">{formatDate(e.createdAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {list.data && list.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {list.data.total} enquiries
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
      {open && <EnquiryDialog key={open.id} enquiry={open} canManage={canManage} onClose={() => setOpen(null)} />}
    </>
  )
}

function EnquiryDialog({ enquiry, canManage, onClose }: { enquiry: Enquiry; canManage: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<EnquiryStatus>(enquiry.status)
  const [note, setNote] = useState(enquiry.staffNote ?? '')
  const save = useMutation({
    mutationFn: () => put<Enquiry>(`/api/enquiries/${enquiry.id}`, { status, staffNote: note.trim() || null }),
    onSuccess: () => {
      toast.success('Enquiry updated.')
      void queryClient.invalidateQueries({ queryKey: ['enquiries'] })
      onClose()
    },
  })

  return (
    <Dialog open onOpenChange={(o) => !o && !save.isPending && onClose()}>
      <DialogContent className="max-h-[90svh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{enquiry.name}</DialogTitle>
          <DialogDescription>
            Received {formatDateTime(enquiry.createdAtUtc)}
            {enquiry.viewedAtUtc ? ` · first viewed ${formatDateTime(enquiry.viewedAtUtc)}${enquiry.viewedBy ? ` by ${enquiry.viewedBy}` : ''}` : ''}
          </DialogDescription>
        </DialogHeader>
        <dl className="grid gap-3 text-sm">
          <div className="flex flex-wrap gap-3">
            {enquiry.phone && (
              <a href={`tel:${enquiry.phone.replace(/\s/g, '')}`} className="inline-flex items-center gap-1.5 text-primary hover:underline">
                <Phone className="size-4" /> {enquiry.phone}
              </a>
            )}
            {enquiry.email && (
              <a href={`mailto:${enquiry.email}`} className="inline-flex items-center gap-1.5 text-primary hover:underline">
                <Mail className="size-4" /> {enquiry.email}
              </a>
            )}
          </div>
          <Row label="Highest qualification" value={`${qualificationLabel(enquiry.highestQualification)}, ${enquiry.graduationYear}`} />
          <Row label="Preferred programme" value={enquiry.preferredProgramme ?? 'Not sure, wants guidance'} />
          {enquiry.preferredProgramme && enquiry.openToGuidance && <Row label="Guidance" value="Also open to guidance" />}
          <Row label="Canada and Germany" value={destinationLabel(enquiry.destinationInterest)} />
          {enquiry.message && <Row label="Message" value={enquiry.message} />}
        </dl>
        {canManage ? (
          <div className="grid gap-4 border-t pt-4">
            <SelectField label="Status" options={statuses} placeholder="Choose…" value={status} onChange={(e) => setStatus(e.target.value as EnquiryStatus)} />
            <div className="grid gap-1.5">
              <Label htmlFor="enq-note">Follow-up note</Label>
              <textarea id="enq-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000} className="w-full rounded-lg border border-input bg-card px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50" />
              {enquiry.handledBy && <p className="text-xs text-muted-foreground">Last updated by {enquiry.handledBy}.</p>}
            </div>
            <FormError message={save.error ? (save.error instanceof ApiError ? save.error.message : 'Something went wrong. Please try again.') : null} />
          </div>
        ) : (
          enquiry.staffNote && <Row label="Follow-up note" value={enquiry.staffNote} />
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>
            {canManage ? 'Cancel' : 'Close'}
          </Button>
          {canManage && (
            <Button onClick={() => save.mutate()} disabled={save.isPending}>
              Save
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-0.5 whitespace-pre-wrap text-ink">{value}</dd>
    </div>
  )
}
