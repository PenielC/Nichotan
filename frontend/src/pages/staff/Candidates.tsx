import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ArrowLeft, ChevronLeft, ChevronRight, Search } from 'lucide-react'
import { FormError } from '@/components/form'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { PageHeader } from '@/components/page-header'
import { ApplicationFormCard } from '@/pages/staff/Applications'
import { TimelineCard } from '@/components/timeline'
import { CandidateAdmissionsCard } from '@/pages/staff/Admissions'
import { TasksCard } from '@/pages/staff/Tasks'
import { CandidateDocumentsCard } from '@/pages/staff/Documents'
import { ApiError, get, query, type CandidateProfile, type CandidateSummary, type Paged } from '@/lib/api'
import { formatDate, formatDateTime } from '@/lib/format'

const pageSize = 25

export function CandidateList() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const [text, setText] = useState(search)

  // Search as people type, after a short pause.
  useEffect(() => {
    const timer = setTimeout(() => {
      if (text.trim() !== search) setParams(text.trim() ? { search: text.trim() } : {}, { replace: true })
    }, 300)
    return () => clearTimeout(timer)
  }, [text, search, setParams])

  const list = useQuery({
    queryKey: ['candidates', 'list', search, page, pageSize],
    queryFn: () => get<Paged<CandidateSummary>>(`/api/candidates${query({ search, page, pageSize })}`),
    placeholderData: keepPreviousData,
  })
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / pageSize)) : 1
  const goTo = (p: number) => setParams({ ...(search ? { search } : {}), page: String(p) })

  return (
    <>
      <PageHeader title="Candidates" description="Everyone who has registered on the candidate portal." />
      <Card>
        <CardContent className="grid gap-4">
          <label className="relative max-w-md">
            <span className="sr-only">Search candidates</span>
            <Search className="absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
            <Input value={text} onChange={(e) => setText(e.target.value)} placeholder="Search by name or email" className="h-10 pl-9" maxLength={100} />
          </label>

          {list.isError ? (
            <FormError message="We couldn’t load candidates. Please refresh the page." />
          ) : !list.data ? (
            <Skeleton className="h-64" />
          ) : list.data.items.length === 0 ? (
            <p className="py-10 text-center text-sm text-muted-foreground">{search ? `No candidates match “${search}”.` : 'No candidates have registered yet.'}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead className="hidden md:table-cell">Phone</TableHead>
                  <TableHead className="text-right">Registered</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.data.items.map((c) => (
                  <TableRow key={c.id} className="cursor-pointer" onClick={() => navigate(`/staff/candidates/${c.id}`)}>
                    <TableCell className="font-medium text-ink">
                      <Link to={`/staff/candidates/${c.id}`} onClick={(e) => e.stopPropagation()} className="hover:underline">
                        {c.firstName} {c.lastName}
                      </Link>
                    </TableCell>
                    <TableCell>{c.email}</TableCell>
                    <TableCell className="hidden md:table-cell">{c.phone ?? '—'}</TableCell>
                    <TableCell className="text-right text-muted-foreground">{formatDate(c.createdAtUtc)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          {list.data && list.data.total > pageSize && (
            <div className="flex items-center justify-between text-sm text-muted-foreground">
              <span>
                Page {page} of {pages} · {list.data.total} candidates
              </span>
              <div className="flex gap-2">
                <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => goTo(page - 1)}>
                  <ChevronLeft /> Previous
                </Button>
                <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => goTo(page + 1)}>
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

export function CandidateDetail() {
  const { id = '' } = useParams()
  // Opening a record is audited on the server, so it isn't refetched on window focus.
  const candidate = useQuery({ queryKey: ['candidates', id], queryFn: () => get<CandidateProfile>(`/api/candidates/${id}`), refetchOnWindowFocus: false, retry: false })

  return (
    <>
      <Button asChild variant="ghost" size="sm" className="mb-4 -ml-2">
        <Link to="/staff/candidates">
          <ArrowLeft /> Candidates
        </Link>
      </Button>
      {candidate.isError ? (
        <FormError message={candidate.error instanceof ApiError && candidate.error.status === 404 ? 'This candidate doesn’t exist.' : 'We couldn’t load this candidate.'} />
      ) : !candidate.data ? (
        <Skeleton className="h-64" />
      ) : (
        <>
          <PageHeader title={`${candidate.data.firstName} ${candidate.data.lastName}`} description={`Registered ${formatDateTime(candidate.data.createdAtUtc)}`} />
          <Card>
            <CardHeader>
              <CardTitle className="text-ink">Contact details</CardTitle>
              <CardDescription>Admissions, documents, visas, tasks and a timeline are further down this record.</CardDescription>
            </CardHeader>
            <CardContent>
              <dl className="grid gap-4 sm:grid-cols-2">
                <Detail label="First name" value={candidate.data.firstName} />
                <Detail label="Last or family name" value={candidate.data.lastName} />
                <Detail label="Email" value={candidate.data.email} />
                <Detail label="Phone" value={candidate.data.phone ?? 'Not given'} />
              </dl>
            </CardContent>
          </Card>
          <div className="mt-6 grid gap-6">
            <CandidateAdmissionsCard candidateId={candidate.data.id} />
            <ApplicationFormCard candidateId={candidate.data.id} />
            <CandidateDocumentsCard candidateId={candidate.data.id} />
            <TasksCard candidateId={candidate.data.id} />
            <TimelineCard candidateId={candidate.data.id} />
          </div>
        </>
      )}
    </>
  )
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-1 text-sm text-ink">{value}</dd>
    </div>
  )
}
