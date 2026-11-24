import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowRight, Award, BadgeCheck, FileCheck2, GraduationCap, MailCheck, MessageCircleQuestion, ScrollText, UserPlus, Users } from 'lucide-react'
import { BriefingCard } from '@/components/briefing-card'
import { Journey } from '@/components/journey'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { PageHeader } from '@/components/page-header'
import { get, query, type CandidateSummary, type Paged, type StaffMember } from '@/lib/api'
import { caseWorkerRoles, hasAnyRole, useAuth } from '@/lib/auth'
import { ViewedBadge } from '@/pages/staff/Enquiries'
import { enquirySourceLabel, qualificationLabel, type EnquirySummary } from '@/lib/enquiries'
import { intakeText, type AdmissionStats, type AdmissionSummary, type PalFollowUp } from '@/lib/admissions'
import { formatDate } from '@/lib/format'
import type { StaffTask, TaskSummary } from '@/lib/tasks'
import { visaStatusLabel, type BiometricsFollowUp, type VisaStats } from '@/lib/visa'

function greeting(hour: number) {
  return hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'
}

export default function Dashboard() {
  const { state } = useAuth()
  const [hello] = useState(() => greeting(new Date().getHours()))
  const session = state.status === 'signedIn' ? state.session : null
  const caseWorker = !!session && hasAnyRole(session, caseWorkerRoles)
  const admin = !!session && hasAnyRole(session, ['Administrator'])

  const recent = useQuery({ queryKey: ['candidates', 'list', '', 1, 5], queryFn: () => get<Paged<CandidateSummary>>(`/api/candidates${query({ page: 1, pageSize: 5 })}`), enabled: caseWorker })
  const enquiries = useQuery({ queryKey: ['enquiries', 'summary'], queryFn: () => get<EnquirySummary>('/api/enquiries/summary'), enabled: caseWorker, refetchInterval: 60_000 })
  const admissions = useQuery({ queryKey: ['admissions', 'stats'], queryFn: () => get<AdmissionStats>('/api/admissions/stats'), enabled: caseWorker, refetchInterval: 60_000 })
  const visa = useQuery({ queryKey: ['visa', 'stats'], queryFn: () => get<VisaStats>('/api/visa-cases/stats'), enabled: caseWorker, refetchInterval: 60_000 })
  const biometrics = useQuery({ queryKey: ['visa', 'biometrics-follow-ups'], queryFn: () => get<BiometricsFollowUp[]>('/api/visa-cases/biometrics-follow-ups'), enabled: caseWorker, refetchInterval: 60_000 })
  const taskSummary = useQuery({ queryKey: ['tasks', 'summary'], queryFn: () => get<TaskSummary>('/api/tasks/summary'), enabled: caseWorker, refetchInterval: 60_000 })
  const myTasks = useQuery({ queryKey: ['tasks', 'list', 'mine', 'Open', 'dashboard'], queryFn: () => get<Paged<StaffTask>>(`/api/tasks${query({ mine: 'true', status: 'Open', pageSize: 5 })}`), enabled: caseWorker, refetchInterval: 60_000 })
  const palFollowUps = useQuery({ queryKey: ['admissions', 'pal-follow-ups'], queryFn: () => get<PalFollowUp[]>('/api/admissions/pal-follow-ups'), enabled: caseWorker, refetchInterval: 60_000 })
  const toReview = useQuery({ queryKey: ['admissions', 'list', 'review'], queryFn: () => get<Paged<AdmissionSummary>>(`/api/admissions${query({ status: 'ReadyForReview', pageSize: 5 })}`), enabled: caseWorker, refetchInterval: 60_000 })
  const staff = useQuery({ queryKey: ['staff'], queryFn: () => get<StaffMember[]>('/api/staff'), enabled: admin })

  if (!session) return null

  return (
    <>
      <PageHeader title={`${hello}, ${session.user.firstName}`} description="Here’s what’s happening with your students today." />

      {caseWorker && <BriefingCard />}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {caseWorker && (
          <Stat
            icon={GraduationCap}
            label="Admissions"
            value={admissions.data?.active}
            loading={admissions.isLoading}
            hint="Open applications"
            to="/staff/admissions"
          />
        )}
        {caseWorker && <Stat icon={Award} label="Offers issued" value={admissions.data?.offersIssued} loading={admissions.isLoading} hint="Applications that have received an offer" />}
        {caseWorker && <Stat icon={MailCheck} label="Acceptance letters" value={admissions.data?.acceptances} loading={admissions.isLoading} hint="Formal acceptance letters received" />}
        {caseWorker && <Stat icon={FileCheck2} label="Visa applications in process" value={visa.data?.inProcess} loading={visa.isLoading} hint="Submitted and not yet decided" to="/staff/visa" />}
        {caseWorker && <Stat icon={BadgeCheck} label="Approved visas" value={visa.data?.approved} loading={visa.isLoading} hint="Visa approvals recorded" to="/staff/visa?status=Approved" />}
        {caseWorker && <Stat icon={Users} label="Registered candidates" value={recent.data?.total} loading={recent.isLoading} />}
        {caseWorker && <Stat icon={MessageCircleQuestion} label="Enquiries not yet viewed" value={enquiries.data?.unviewed} loading={enquiries.isLoading} />}
        {admin && <Stat icon={UserPlus} label="Invitations pending" value={staff.data?.filter((s) => s.invitationPending && !s.isDisabled).length} loading={staff.isLoading} />}
      </div>

      {caseWorker && (
        <Card className="mt-6">
          <CardHeader>
            <CardTitle className="text-ink">
              My tasks{taskSummary.data ? ` (${taskSummary.data.mineOpen}${taskSummary.data.mineOverdue ? `, ${taskSummary.data.mineOverdue} overdue` : ''})` : ''}
            </CardTitle>
            <CardDescription>What’s next for you, overdue first.</CardDescription>
            <CardAction>
              <Button asChild variant="link" size="sm">
                <Link to="/staff/tasks">
                  All tasks <ArrowRight />
                </Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>
            {myTasks.isLoading ? (
              <Skeleton className="h-16" />
            ) : myTasks.data?.items.length ? (
              <ul className="divide-y">
                {myTasks.data.items.map((t) => (
                  <li key={t.id}>
                    <Link to={t.candidateId ? `/staff/candidates/${t.candidateId}` : '/staff/tasks'} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                      <span className="min-w-0 text-sm">
                        <span className="font-medium text-ink">{t.title}</span>
                        {t.candidateName && <span className="block text-xs text-muted-foreground">{t.candidateName}</span>}
                      </span>
                      <span className={t.overdue ? 'text-xs font-semibold text-destructive' : 'text-xs text-muted-foreground'}>{t.dueOn ? `${t.overdue ? 'Overdue · ' : 'Due '}${formatDate(t.dueOn)}` : 'No due day'}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="py-4 text-center text-sm text-muted-foreground">You have no open tasks.</p>
            )}
          </CardContent>
        </Card>
      )}

      {caseWorker && !!biometrics.data?.length && (
        <Card className="mt-6 border-amber-300">
          <CardHeader>
            <CardTitle className="text-ink">Biometrics to follow up ({biometrics.data.length})</CardTitle>
            <CardDescription>The visa application is submitted but biometrics aren’t marked done. Remind the student, then record it when it’s done (or when they say so).</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="divide-y">
              {biometrics.data.map((b) => (
                <li key={b.visaCaseId}>
                  <Link to={`/staff/visa/${b.visaCaseId}`} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                    <span className="min-w-0 text-sm">
                      <span className="font-medium text-ink">{b.candidateName}</span>
                      <span className="block text-xs text-muted-foreground">
                        {b.institutionName} · {visaStatusLabel[b.status]}
                        {b.appointmentOn ? ` · appointment ${formatDate(b.appointmentOn)}` : ''}
                      </span>
                    </span>
                    <span className={b.overdue ? 'text-xs font-semibold text-destructive' : 'text-xs text-muted-foreground'}>
                      {b.overdue ? 'Overdue · ' : ''}
                      {b.daysWaiting === 0 ? 'submitted today' : `${b.daysWaiting} day${b.daysWaiting === 1 ? '' : 's'} since submitted`}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {caseWorker && !!palFollowUps.data?.length && (
        <Card className="mt-6 border-amber-300">
          <CardHeader>
            <CardTitle className="text-ink">PAL letters to follow up ({palFollowUps.data.length})</CardTitle>
            <CardDescription>The acceptance letter has arrived but the school hasn’t sent the PAL. Chase the school, then record it.</CardDescription>
          </CardHeader>
          <CardContent>
            <ul className="divide-y">
              {palFollowUps.data.map((p) => (
                <li key={p.applicationId}>
                  <Link to={`/staff/admissions/${p.applicationId}`} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                    <span className="min-w-0 text-sm">
                      <span className="font-medium text-ink">{p.candidateName}</span>
                      <span className="block text-xs text-muted-foreground">
                        {p.institutionName} · {p.programmeName} · acceptance received {formatDate(p.acceptanceReceivedOn)}
                      </span>
                    </span>
                    <span className={p.overdue ? 'text-xs font-semibold text-destructive' : 'text-xs text-muted-foreground'}>
                      {p.overdue ? 'Overdue · ' : ''}
                      {p.daysWaiting === 0 ? 'acceptance today' : `${p.daysWaiting} day${p.daysWaiting === 1 ? '' : 's'} without a PAL`}
                    </span>
                  </Link>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}

      {caseWorker && (
        <Card className="mt-6">
          <CardHeader>
            <CardTitle className="text-ink">Applications to review{toReview.data ? ` (${toReview.data.total})` : ''}</CardTitle>
            <CardDescription>Sent by students. Check the documents, then apply on the school’s website and record it.</CardDescription>
            <CardAction>
              <Button asChild variant="link" size="sm">
                <Link to="/staff/admissions?status=ReadyForReview">
                  View all <ArrowRight />
                </Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>
            {toReview.isLoading ? (
              <Skeleton className="h-20" />
            ) : toReview.data?.items.length ? (
              <ul className="divide-y">
                {toReview.data.items.map((a) => (
                  <li key={a.id}>
                    <Link to={`/staff/admissions/${a.id}`} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                      <span className="min-w-0 text-sm">
                        <span className="font-medium text-ink">{a.candidateName}</span>
                        <span className="block text-xs text-muted-foreground">
                          {a.institutionName} · {a.programmeName} · {intakeText(a.intakeTerm, a.intakeYear)}
                        </span>
                      </span>
                      <span className="text-xs text-muted-foreground">Sent {formatDate(a.statusChangedAtUtc)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="py-4 text-center text-sm text-muted-foreground">Nothing is waiting for review.</p>
            )}
          </CardContent>
        </Card>
      )}

      {caseWorker && (
        <Card className="mt-6">
          <CardHeader>
            <CardTitle className="text-ink">New enquiries</CardTitle>
            <CardDescription>People who asked about studying abroad on the website and haven’t been looked at yet.</CardDescription>
            <CardAction>
              <Button asChild variant="link" size="sm">
                <Link to="/staff/enquiries">
                  Review enquiries <ArrowRight />
                </Link>
              </Button>
            </CardAction>
          </CardHeader>
          <CardContent>
            {enquiries.isLoading ? (
              <Skeleton className="h-24" />
            ) : enquiries.data?.latest.length ? (
              <ul className="divide-y">
                {enquiries.data.latest.map((q) => (
                  <li key={q.id}>
                    <Link to={`/staff/enquiries?search=${encodeURIComponent(q.name)}`} className="flex flex-wrap items-center justify-between gap-2 py-3 hover:bg-accent/40">
                      <span className="min-w-0 text-sm">
                        <span className="font-medium text-ink">{q.name}</span>
                        <span className="block text-xs text-muted-foreground">
                          {q.source === 'WebsiteContactForm' ? enquirySourceLabel[q.source] : `${q.preferredProgramme ?? 'Wants guidance'} · ${qualificationLabel(q.highestQualification)}`} · {formatDate(q.createdAtUtc)}
                        </span>
                      </span>
                      <ViewedBadge enquiry={q} />
                    </Link>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="py-6 text-center text-sm text-muted-foreground">Every enquiry has been viewed.</p>
            )}
          </CardContent>
        </Card>
      )}

      <div className="mt-6 grid gap-6 xl:grid-cols-[2fr_1fr]">
        {caseWorker && (
          <Card>
            <CardHeader>
              <CardTitle className="text-ink">Recent candidates</CardTitle>
              <CardDescription>Newest registrations first.</CardDescription>
              <CardAction>
                <Button asChild variant="link" size="sm">
                  <Link to="/staff/candidates">
                    View all <ArrowRight />
                  </Link>
                </Button>
              </CardAction>
            </CardHeader>
            <CardContent>
              {recent.isLoading ? (
                <Skeleton className="h-40" />
              ) : recent.data?.items.length ? (
                <ul className="divide-y">
                  {recent.data.items.map((c) => (
                    <li key={c.id}>
                      <Link to={`/staff/candidates/${c.id}`} className="flex items-center gap-3 py-3 hover:bg-muted/50">
                        <span className="flex size-9 items-center justify-center rounded-full bg-secondary text-xs font-semibold text-ink">
                          {c.firstName.charAt(0)}
                          {c.lastName.charAt(0)}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium text-ink">
                            {c.firstName} {c.lastName}
                          </span>
                          <span className="block truncate text-xs text-muted-foreground">{c.email}</span>
                        </span>
                        <span className="text-xs text-muted-foreground">{formatDate(c.createdAtUtc)}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="py-6 text-center text-sm text-muted-foreground">No candidates have registered yet.</p>
              )}
            </CardContent>
          </Card>
        )}

        {admin && (
          <Card>
            <CardHeader>
              <CardTitle className="text-ink">Quick actions</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-2">
              <Button asChild variant="outline" className="justify-start">
                <Link to="/staff/team?invite=1">
                  <UserPlus /> Invite a staff member
                </Link>
              </Button>
              <Button asChild variant="outline" className="justify-start">
                <Link to="/staff/audit">
                  <ScrollText /> Review the audit log
                </Link>
              </Button>
            </CardContent>
          </Card>
        )}
      </div>

      <Card className="mt-6">
        <CardHeader>
          <CardTitle className="text-ink">The Nichotan student journey</CardTitle>
          <CardDescription>Application, offer, visa and pre-departure tracking arrive in the next milestones; each stage will show live counts here.</CardDescription>
        </CardHeader>
        <CardContent>
          <Journey current={null} />
        </CardContent>
      </Card>
    </>
  )
}

function Stat({ icon: Icon, label, value, loading, hint, to }: { icon: typeof Users; label: string; value: number | undefined; loading: boolean; hint?: string; to?: string }) {
  const body = (
    <CardContent className="flex items-center gap-4">
      <span className="flex size-11 shrink-0 items-center justify-center rounded-xl bg-accent text-primary">
        <Icon className="size-5" aria-hidden />
      </span>
      <div className="min-w-0">
        <p className="text-sm text-muted-foreground">{label}</p>
        {loading ? <Skeleton className="mt-1 h-7 w-12" /> : <p className="text-2xl font-bold text-ink">{value ?? '–'}</p>}
        {hint && !loading && <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p>}
      </div>
    </CardContent>
  )
  return <Card className={to ? 'transition-colors hover:bg-accent/30' : undefined}>{to ? <Link to={to} className="block rounded-xl focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none">{body}</Link> : body}</Card>
}
