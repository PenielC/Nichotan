import type { ReactNode } from 'react'
import { Badge } from '@/components/ui/badge'
import { levelLabel, startTerms, statusLabel, type FormStatus, type ServerFormData } from '@/lib/application'
import { cn } from '@/lib/utils'

const dash = '—'
const monthYear = (v?: string | null) => (v && /^\d{4}-\d{2}$/.test(v) ? `${v.slice(5)}/${v.slice(0, 4)}` : (v ?? ''))
const dateText = (v?: string | null) => (v ? new Date(`${v}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }) : dash)

export function FormStatusBadge({ status, className }: { status: FormStatus; className?: string }) {
  return (
    <Badge variant={status === 'Submitted' ? 'default' : status === 'Reopened' ? 'destructive' : 'secondary'} className={className}>
      {statusLabel[status]}
    </Badge>
  )
}

function Item({ label, value }: { label: string; value?: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">{label}</dt>
      <dd className="mt-0.5 text-sm break-words text-ink">{value === undefined || value === null || value === '' ? dash : value}</dd>
    </div>
  )
}

function Block({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return (
    <section className="grid gap-3">
      <h3 className="border-b pb-1.5 text-sm font-semibold text-ink">{title}</h3>
      <dl className={cn('grid gap-x-6 gap-y-3 sm:grid-cols-2 lg:grid-cols-3', className)}>{children}</dl>
    </section>
  )
}

/** Everything on the form, read-only: used to review before signing, and to view a submitted version. */
export function FormSummary({ data }: { data: ServerFormData }) {
  const { personal: p, contact: c, program: g, academic: a } = data
  const institutions = (g.institutions ?? []).filter(Boolean)
  const choices = (g.programChoices ?? []).filter(Boolean)
  const schools = (a.highSchools ?? []).filter((h) => h.name)
  const colleges = (a.postSecondary ?? []).filter((x) => x.institution)

  return (
    <div className="grid gap-6">
      <Block title="Personal information">
        <Item label="First name" value={p.firstName} />
        <Item label="Middle name(s)" value={p.middleNames} />
        <Item label="Last or family name" value={p.lastName} />
        <Item label="Gender" value={p.gender} />
        <Item label="Date of birth" value={dateText(p.birthDate)} />
        <Item label="Country of citizenship" value={p.citizenshipCountry} />
      </Block>
      <Block title="Contact information">
        <Item label="Home address" value={c.homeAddress} />
        <Item label="City" value={c.city} />
        <Item label="Country" value={c.country} />
        <Item label="Email" value={c.email} />
        <Item label="Phone" value={c.phone} />
        <Item label="Alternative phone" value={c.alternativePhone} />
        <Item label="Next of kin" value={c.nextOfKinName} />
        <Item label="Next of kin email" value={c.nextOfKinEmail} />
        <Item label="Next of kin phone" value={c.nextOfKinPhone} />
      </Block>
      <Block title="Program and institutions">
        <Item label="Destination" value={g.destinationCountry} />
        <Item label="Program level" value={levelLabel(g.programLevel)} />
        <Item label="Start" value={g.startYear ? `${startTerms.find((t) => t.value === g.startTerm)?.label ?? ''} ${g.startYear}`.trim() : undefined} />
        <Item
          label="Institutions, in order of preference"
          value={institutions.length ? <ol className="list-inside list-decimal">{institutions.map((v, i) => <li key={i}>{v}</li>)}</ol> : undefined}
        />
        <Item label="Program choices" value={choices.length ? <ol className="list-inside list-decimal">{choices.map((v, i) => <li key={i}>{v}</li>)}</ol> : undefined} />
      </Block>
      <Block title="Academic history" className="lg:grid-cols-2">
        {schools.map((h, i) => (
          <Item key={`s${i}`} label={`High school ${i + 1}`} value={`${h.name}, ${h.provinceCountry ?? ''} · ${monthYear(h.started)} – ${h.completed ? monthYear(h.completed) : 'present'} · ${h.grade === 'LessThan12' ? 'Less than 12' : h.grade ? '12 or equivalent' : ''}`} />
        ))}
        {colleges.map((x, i) => (
          <Item
            key={`c${i}`}
            label={`Post-secondary ${i + 1}`}
            value={`${x.institution}, ${x.provinceCountry ?? ''} · ${monthYear(x.started)} – ${x.completed ? monthYear(x.completed) : 'present'}${x.credentialAwarded ? ` · ${x.credentialAwarded}` : ''}`}
          />
        ))}
        {schools.length === 0 && colleges.length === 0 && <Item label="Schools and colleges" value={undefined} />}
      </Block>
    </div>
  )
}
