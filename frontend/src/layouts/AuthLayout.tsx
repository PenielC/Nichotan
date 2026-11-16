import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Compass, GraduationCap, Plane, Route } from 'lucide-react'
import studentHero from '@/assets/student-hero.jpg'
import { Crest, Logo, Wordmark } from '@/components/brand'

const promises = [
  { icon: Compass, label: 'Expert guidance' },
  { icon: GraduationCap, label: 'University applications' },
  { icon: Plane, label: 'Visa support' },
  { icon: Route, label: 'End-to-end tracking' },
]

/** Sign-in and account pages: a navy brand panel beside a form card (the panel hides on small screens). */
export function AuthLayout({ title, subtitle, children, footer }: { title: string; subtitle?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="grid min-h-svh lg:grid-cols-[1.1fr_1fr]">
      <aside className="relative hidden overflow-hidden bg-ink text-white lg:flex lg:flex-col lg:gap-10 lg:p-12">
        <div aria-hidden className="absolute -right-24 -bottom-24 size-96 rounded-full bg-leaf/15 blur-3xl" />
        <div aria-hidden className="absolute -top-32 -left-16 size-80 rounded-full bg-[#1d4a8f]/40 blur-3xl" />
        <img
          src={studentHero}
          alt=""
          aria-hidden
          className="pointer-events-none absolute right-0 bottom-0 h-auto w-full [mask-image:linear-gradient(to_bottom,transparent,black_40%)]"
        />
        <div aria-hidden className="absolute inset-x-0 bottom-0 h-40 bg-gradient-to-t from-ink to-transparent" />
        <Link to="/" className="relative">
          <Logo onDark tagline className="text-xl [&>span:first-child]:size-16" />
        </Link>
        <div className="relative max-w-md">
          <h2 className="text-4xl leading-tight font-bold">
            From application to a <span className="text-leaf">brighter future</span>
          </h2>
          <p className="mt-4 text-lg text-white/80">We help you achieve your study abroad dreams, one clear step at a time.</p>
        </div>
        <ul className="relative mt-auto grid grid-cols-4 gap-4 border-t border-white/15 pt-8">
          {promises.map(({ icon: Icon, label }) => (
            <li key={label} className="flex flex-col items-start gap-2 text-sm text-white/85">
              <Icon className="size-6 text-leaf" aria-hidden />
              {label}
            </li>
          ))}
        </ul>
      </aside>

      <main className="flex items-center justify-center px-4 py-10 sm:px-8">
        <div className="w-full max-w-md">
          <div className="mb-8 flex flex-col items-center text-center">
            <Link to="/" className="mb-6 inline-flex items-center gap-3 lg:hidden">
              <Crest />
              <Wordmark tagline />
            </Link>
            <h1 className="text-2xl font-bold text-ink">{title}</h1>
            {subtitle && <p className="mt-1.5 text-muted-foreground">{subtitle}</p>}
          </div>
          <div className="rounded-2xl bg-card p-6 shadow-sm ring-1 ring-foreground/5 sm:p-8">{children}</div>
          {footer && <div className="mt-6 text-center text-sm text-muted-foreground">{footer}</div>}
        </div>
      </main>
    </div>
  )
}
