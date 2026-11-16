import { Link } from 'react-router-dom'
import { ArrowRight, Compass, GraduationCap, Plane, Route } from 'lucide-react'
import studentHero from '@/assets/student-hero.jpg'
import { Logo } from '@/components/brand'
import { EnquirySection } from '@/components/enquiry-section'
import { Journey } from '@/components/journey'
import { Button } from '@/components/ui/button'
import { homePath, useAuth } from '@/lib/auth'

const year = new Date().getFullYear()

const services = [
  { icon: Compass, title: 'Expert guidance', text: 'Counsellors who help you choose the right course and institution.' },
  { icon: GraduationCap, title: 'University applications', text: 'Your applications prepared, submitted and followed up.' },
  { icon: Plane, title: 'Visa support', text: 'Help with study permits, biometrics and every visa step.' },
  { icon: Route, title: 'End-to-end tracking', text: 'See where your application stands at any time.' },
]

export default function Landing() {
  const { state } = useAuth()

  return (
    <div className="min-h-svh bg-background">
      <section className="relative overflow-hidden bg-ink text-white">
        <div aria-hidden className="absolute -right-40 -bottom-40 size-[36rem] rounded-full bg-leaf/15 blur-3xl" />
        <div aria-hidden className="absolute -top-40 -left-20 size-[28rem] rounded-full bg-[#1d4a8f]/40 blur-3xl" />
        <header className="relative z-10 mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-6 sm:px-6">
          <Logo onDark tagline className="text-sm sm:text-base" />
          <nav className="flex items-center gap-2">
            {state.status === 'signedIn' ? (
              <Button asChild className="bg-leaf text-ink hover:bg-leaf/90">
                <Link to={homePath(state.session)}>
                  Open my account <ArrowRight />
                </Link>
              </Button>
            ) : (
              <>
                <Button asChild variant="ghost" className="hidden text-white hover:bg-white/10 hover:text-white sm:inline-flex">
                  <a href="#enquire">Enquire</a>
                </Button>
                <Button asChild variant="ghost" className="text-white hover:bg-white/10 hover:text-white">
                  <Link to="/login">Sign in</Link>
                </Button>
                <Button asChild className="hidden bg-leaf text-ink hover:bg-leaf/90 sm:inline-flex">
                  <Link to="/register">Create an account</Link>
                </Button>
              </>
            )}
          </nav>
        </header>

        <img
          src={studentHero}
          alt=""
          aria-hidden
          className="pointer-events-none absolute top-0 right-0 hidden h-full w-auto max-w-[55%] object-cover object-right [mask-image:linear-gradient(to_right,transparent,black_35%)] lg:block"
        />
        <div className="relative mx-auto max-w-6xl px-4 pt-12 pb-20 sm:px-6 sm:pt-20 sm:pb-28">
          <h1 className="max-w-2xl text-4xl leading-tight font-bold sm:text-6xl">
            From application to a <span className="text-leaf">brighter future</span>
          </h1>
          <p className="mt-6 max-w-xl text-lg text-white/80">We help you achieve your study abroad dreams, with expert support from your first application to the day you fly.</p>
          <div className="mt-10 flex flex-wrap gap-3">
            <Button asChild size="lg" className="h-12 bg-leaf px-6 text-base text-ink hover:bg-leaf/90">
              <Link to="/register">
                Start your journey <ArrowRight />
              </Link>
            </Button>
            <Button asChild size="lg" variant="outline" className="h-12 border-white/30 bg-transparent px-6 text-base text-white hover:bg-white/10 hover:text-white">
              <Link to="/login">I already have an account</Link>
            </Button>
            <Button asChild size="lg" variant="ghost" className="h-12 px-6 text-base text-white hover:bg-white/10 hover:text-white">
              <a href="#enquire">Make an enquiry</a>
            </Button>
          </div>
        </div>
      </section>

      <section className="mx-auto grid max-w-6xl gap-6 px-4 py-16 sm:grid-cols-2 sm:px-6 lg:grid-cols-4">
        {services.map(({ icon: Icon, title, text }) => (
          <div key={title}>
            <span className="flex size-12 items-center justify-center rounded-xl bg-accent text-primary">
              <Icon className="size-6" aria-hidden />
            </span>
            <h2 className="mt-4 font-semibold text-ink">{title}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{text}</p>
          </div>
        ))}
      </section>

      <section className="border-t bg-card">
        <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6">
          <h2 className="text-2xl font-bold text-ink">The Nichotan student journey</h2>
          <p className="mt-2 mb-10 text-muted-foreground">Seven clear stages, each one tracked in your portal.</p>
          <Journey current={null} />
        </div>
      </section>

      <EnquirySection />

      <footer className="mx-auto max-w-6xl px-4 py-8 text-sm text-muted-foreground sm:px-6">© {year} Nicho Tan Consulting</footer>
    </div>
  )
}
