import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { FormError, SelectField } from '@/components/form'
import { PageHeader } from '@/components/page-header'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field } from '@/components/form'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { get, query } from '@/lib/api'
import { applicantLevelLabel, destinations, startTerms, type ApplicantLevel } from '@/lib/application'
import { caseWorkerRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { statusLabel, type AdmissionStatus, type Institution } from '@/lib/admissions'
import { dayText } from '@/lib/admissions'
import { visaStatusLabel, type VisaStatus } from '@/lib/visa'
import type { MonthlyReport, StatusCount } from '@/lib/reports'

const levels: ApplicantLevel[] = ['HighSchool', 'Undergraduate', 'Postgraduate']
const thisMonth = () => new Date().toISOString().slice(0, 7)

/** One block of counts with a small bar, so the shape of the pipeline reads at a glance. */
function CountTable({ title, note, rows, label }: { title: string; note: string; rows: StatusCount[]; label: (status: string) => string }) {
  const max = Math.max(1, ...rows.map((r) => r.count))
  const total = rows.reduce((sum, r) => sum + r.count, 0)
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg text-ink">{title}</CardTitle>
        <CardDescription>{note}</CardDescription>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Status</TableHead>
              <TableHead className="w-1/2">
                <span className="sr-only">Share</span>
              </TableHead>
              <TableHead className="text-right">Count</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.status} className={r.count === 0 ? 'text-muted-foreground' : undefined}>
                <TableCell>{label(r.status)}</TableCell>
                <TableCell>
                  <div className="h-2 rounded-full bg-muted" aria-hidden>
                    <div className="h-2 rounded-full bg-primary" style={{ width: `${(r.count / max) * 100}%` }} />
                  </div>
                </TableCell>
                <TableCell className="text-right font-medium tabular-nums">{r.count}</TableCell>
              </TableRow>
            ))}
            <TableRow>
              <TableCell className="font-semibold text-ink">All</TableCell>
              <TableCell />
              <TableCell className="text-right font-semibold text-ink tabular-nums">{total}</TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}

const admissionLabel = (s: string) => statusLabel[s as AdmissionStatus] ?? s
const visaLabel = (s: string) => visaStatusLabel[s as VisaStatus] ?? s

function csvOf(report: MonthlyReport): string {
  const escape = (v: string | number) => `"${String(v).replaceAll('"', '""')}"`
  const rows: (string | number)[][] = [
    ['Report', 'Month', report.month, 'Counted up to', report.asAt],
    [],
    ['Block', 'Status', 'Count'],
    ...report.admissionPipeline.map((r) => ['Admissions: current pipeline (as at ' + report.asAt + ')', admissionLabel(r.status), r.count]),
    ...report.admissionMovement.map((r) => ['Admissions: movement in ' + report.month, admissionLabel(r.status), r.count]),
    ...report.visaPipeline.map((r) => ['Visas: current pipeline (as at ' + report.asAt + ')', visaLabel(r.status), r.count]),
    ...report.visaMovement.map((r) => ['Visas: movement in ' + report.month, visaLabel(r.status), r.count]),
    [],
    ['Conversion', 'Cohort', 'Succeeded', 'Declined', 'Still waiting', 'Definition'],
    ...report.conversions.map((c) => [c.name, c.cohort, c.succeeded, c.declined, c.pending, c.definition]),
    [],
    ['Activity', 'Count'],
    ['Candidates registered (not filtered)', report.activity.registrations],
    ['Enquiries received (not filtered)', report.activity.enquiries],
    ['Acceptance letters recorded', report.activity.acceptancesRecorded],
  ]
  return rows.map((r) => r.map(escape).join(',')).join('\r\n')
}

function saveCsv(report: MonthlyReport) {
  const url = URL.createObjectURL(new Blob(['﻿' + csvOf(report)], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = `nichotan-report-${report.month}.csv`
  a.click()
  URL.revokeObjectURL(url)
}

/** Monthly report: where things stand (current pipeline) kept apart from what happened in the month (movement). Counts only. */
export default function Reports() {
  const { state } = useAuth()
  const caseWorker = state.status === 'signedIn' && hasAnyRole(state.session, caseWorkerRoles)
  const [month, setMonth] = useState(thisMonth)
  const [country, setCountry] = useState('')
  const [level, setLevel] = useState('')
  const [institutionId, setInstitutionId] = useState('')
  const [intakeTerm, setIntakeTerm] = useState('')
  const [intakeYear, setIntakeYear] = useState('')

  const institutions = useQuery({ queryKey: ['institutions', '', '', false], queryFn: () => get<Institution[]>('/api/institutions'), enabled: caseWorker })
  const report = useQuery({
    queryKey: ['reports', 'monthly', month, country, level, institutionId, intakeTerm, intakeYear],
    queryFn: () => get<MonthlyReport>(`/api/reports/monthly${query({ month, country, level, institutionId, intakeTerm, intakeYear })}`),
    enabled: /^\d{4}-\d{2}$/.test(month),
    placeholderData: keepPreviousData,
  })
  const r = report.data

  return (
    <>
      <PageHeader title="Reports" description="A month at a glance. “Current pipeline” is where things stood; “movement” is what happened in the month. They answer different questions, so they are never mixed." />
      <div className="grid gap-6">
        <Card>
          <CardContent className="grid gap-4">
            <div className="flex flex-wrap items-end gap-3">
              <Field label="Month" type="month" max={thisMonth()} value={month} onChange={(e) => setMonth(e.target.value)} className="w-44" />
              <SelectField label="Country" className="w-36" placeholder="All" options={destinations.map((c) => ({ value: c, label: c }))} value={country} onChange={(e) => setCountry(e.target.value)} />
              <SelectField label="Level" className="w-44" placeholder="All levels" options={levels.map((l) => ({ value: l, label: applicantLevelLabel[l] }))} value={level} onChange={(e) => setLevel(e.target.value)} />
              {caseWorker && (
                <SelectField label="Institution" className="w-56" placeholder="All institutions" options={(institutions.data ?? []).map((i) => ({ value: i.id, label: i.name }))} value={institutionId} onChange={(e) => setInstitutionId(e.target.value)} />
              )}
              <SelectField label="Intake" className="w-36" placeholder="Any term" options={startTerms.map((t) => ({ value: t.value, label: t.label }))} value={intakeTerm} onChange={(e) => setIntakeTerm(e.target.value)} />
              <Field label="Intake year" type="number" min={2000} max={2100} value={intakeYear} onChange={(e) => setIntakeYear(e.target.value)} className="w-28" />
              <Button variant="outline" className="ml-auto" disabled={!r} onClick={() => r && saveCsv(r)}>
                <Download /> Download CSV
              </Button>
            </div>
            <p className="text-xs text-muted-foreground">
              Filters apply to admission applications and the visa cases that belong to them. Candidates registered and enquiries received have no country or school yet, so they ignore the filters. Statuses are the ones staff recorded, by the day each thing happened.
            </p>
          </CardContent>
        </Card>

        {report.isError ? (
          <FormError message="We couldn’t load the report. Check the month and try again." />
        ) : !r ? (
          <Skeleton className="h-96" />
        ) : (
          <>
            <div className="grid gap-6 lg:grid-cols-2">
              <CountTable title="Admissions: current pipeline" note={`How many applications were in each status as at ${dayText(r.asAt)}${r.asAt === r.to ? ' (the end of the month)' : ' (today, the month is still running)'}.`} rows={r.admissionPipeline} label={admissionLabel} />
              <CountTable title="Admissions: movement in the month" note={`How many applications moved into each status between ${dayText(r.from)} and ${dayText(r.to)}, by the day it happened. Each application counts once per status. Corrections aren’t movement.`} rows={r.admissionMovement} label={admissionLabel} />
              <CountTable title="Visas: current pipeline" note={`How many visa cases were in each status as at ${dayText(r.asAt)}.`} rows={r.visaPipeline} label={visaLabel} />
              <CountTable title="Visas: movement in the month" note={`How many visa cases moved into each status between ${dayText(r.from)} and ${dayText(r.to)}.`} rows={r.visaMovement} label={visaLabel} />
            </div>

            <Card>
              <CardHeader>
                <CardTitle className="text-lg text-ink">Conversion of the month’s submissions</CardTitle>
                <CardDescription>Each rate is worked out only from what was submitted in this month, and measured at the same point in time, so it can’t be inflated by other months.</CardDescription>
              </CardHeader>
              <CardContent className="grid gap-5">
                {r.conversions.map((c) => (
                  <div key={c.name} className="grid gap-2">
                    <p className="font-medium text-ink">{c.name}</p>
                    <p className="text-xs text-muted-foreground">{c.definition}</p>
                    <div className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
                      <Figure label="Submitted this month" value={c.cohort} />
                      <Figure label="Succeeded" value={c.succeeded} rate={c.cohort ? c.succeeded / c.cohort : null} />
                      <Figure label="Declined" value={c.declined} rate={c.cohort ? c.declined / c.cohort : null} />
                      <Figure label="Still waiting" value={c.pending} rate={c.cohort ? c.pending / c.cohort : null} />
                    </div>
                  </div>
                ))}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-lg text-ink">Other activity in the month</CardTitle>
              </CardHeader>
              <CardContent className="grid grid-cols-1 gap-3 text-sm sm:grid-cols-3">
                <Figure label="Candidates registered (all)" value={r.activity.registrations} />
                <Figure label="Enquiries received (all)" value={r.activity.enquiries} />
                <Figure label="Acceptance letters recorded" value={r.activity.acceptancesRecorded} />
              </CardContent>
            </Card>
          </>
        )}
      </div>
    </>
  )
}

function Figure({ label, value, rate }: { label: string; value: number; rate?: number | null }) {
  return (
    <div className="rounded-lg border p-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-2xl font-bold text-ink tabular-nums">{value}</p>
      {rate !== undefined && <p className="text-xs text-muted-foreground">{rate === null ? 'No rate: nothing was submitted' : `${Math.round(rate * 100)}% of submitted`}</p>}
    </div>
  )
}
