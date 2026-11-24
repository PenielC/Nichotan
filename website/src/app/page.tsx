import Image from "next/image";
import Link from "next/link";
import HeroSlider from "@/components/HeroSlider";
import IntroVideo from "@/components/IntroVideo";
import Certificates from "@/components/Certificates";
import PartnerShowcase from "@/components/PartnerShowcase";
import Section, { Heading } from "@/components/Section";
import ServiceCards from "@/components/ServiceCards";
import Testimonials from "@/components/Testimonials";
import content from "@/content/content.json";
import { internal } from "@/content/site";

const home = content.home;

const heroImages = [
  { src: "/images/hero-1.webp", alt: "A Nicho Tan counsellor holding university brochures while advising a student" },
  { src: "/images/hero-2.webp", alt: "A Nicho Tan graduate celebrating in cap and gown" },
  { src: "/images/hero-3.webp", alt: "Nicho Tan students on a university campus in Calgary" },
];

const strip = [
  { src: "/images/home-1.webp", alt: "Students and parents at a Nicho Tan pre-departure session" },
  { src: "/images/home-2.webp", alt: "Graduates outside the Okanagan College Centre for Learning" },
  { src: "/images/home-3.webp", alt: "Students at the Thompson Rivers University winter pre-departure" },
  { src: "/images/home-4.webp", alt: "The Nicho Tan team at the Thompson Rivers University pre-departure" },
];

const serviceImages = [1, 2, 3, 4, 5].map((n) => ({ src: `/images/service-${n}.webp`, alt: "Nicho Tan students and counsellors at a seminar or event" }));

export default function Home() {
  // Split the intro paragraph at sentence ends; nothing is reworded.
  const [lead, ...rest] = home.intro[0].split(/(?<=\.) /);
  return (
    <>
      <h1 className="sr-only">Nicho Tan Consulting</h1>
      <HeroSlider slides={home.slides.map((s, i) => ({ ...s, src: heroImages[i].src, alt: heroImages[i].alt }))} />

      {/* The three sentences of the intro as three cards that overlap the bottom edge of the carousel (desktop only, so phone controls stay clear). */}
      <section className="relative z-10 lg:-mt-16">
        <div className="mx-auto max-w-6xl px-4 pt-10 lg:pt-0">
          <ul className="grid gap-5 lg:grid-cols-3">
            <li className="relative overflow-hidden rounded-2xl bg-ink p-8 text-white shadow-2xl">
              <span aria-hidden className="absolute inset-x-0 top-0 h-1.5 bg-gradient-to-r from-forest to-leaf" />
              <p className="text-lg font-semibold leading-snug md:text-xl">{lead}</p>
            </li>
            {rest.map((text, i) => (
              <li key={text} className="relative overflow-hidden rounded-2xl bg-surface p-8 shadow-2xl ring-1 ring-line">
                <span aria-hidden className="absolute inset-x-0 top-0 h-1.5 bg-gradient-to-r from-forest to-leaf" />
                <span aria-hidden className="mb-4 grid h-12 w-12 place-items-center rounded-xl bg-wash text-forest">
                  {i === 0 ? (
                    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="8" r="4" /><path d="M4 21c0-4.4 3.6-8 8-8s8 3.6 8 8" /></svg>
                  ) : (
                    <svg viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 21s-7-4.4-9.3-9A5.3 5.3 0 0 1 12 6a5.3 5.3 0 0 1 9.3 6c-2.3 4.6-9.3 9-9.3 9Z" /></svg>
                  )}
                </span>
                <p className="leading-relaxed text-muted">{text}</p>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section>
        <div className="mx-auto max-w-6xl px-4 py-14">
          <ul className="grid grid-cols-2 gap-3 md:grid-cols-4 md:gap-4">
            {strip.map((im) => (
              <li key={im.src} className="group overflow-hidden rounded-2xl shadow-md">
                <Image src={im.src} alt={im.alt} width={1000} height={750} loading="lazy" className="aspect-[4/3] w-full object-cover transition duration-500 group-hover:scale-105" />
              </li>
            ))}
          </ul>
        </div>
      </section>

      <Section tone="wash" className="relative overflow-hidden">
        <div aria-hidden className="pointer-events-none absolute -right-24 -top-24 h-80 w-80 rounded-full bg-leaf/20 blur-3xl" />
        <div className="relative">
          <Heading>{home.servicesHeading}</Heading>
          <p className="mb-10 max-w-3xl text-lg text-muted">{home.servicesIntro}</p>
          <ServiceCards items={home.services} images={serviceImages} />
          <Link href={internal(home.servicesButton.href)} className="mt-10 inline-block rounded-full bg-forest px-8 py-3 font-semibold text-on-forest shadow-lg transition hover:-translate-y-0.5 hover:brightness-110">
            {home.servicesButton.label}
          </Link>
        </div>
      </Section>

      <section className="bg-surface py-14 md:py-20">
        <div className="mx-auto max-w-6xl px-4">
          <Heading center>{home.partnersHeading}</Heading>
        </div>
        <PartnerShowcase />
      </section>

      <Section tone="wash">
        <Heading center>Certification</Heading>
        <Certificates />
      </Section>

      <Section>
        <Heading center>{home.testimonialsHeading}</Heading>
        <Testimonials items={home.testimonials} />
      </Section>

      <Section tone="navy">
        <Heading center light>See how we help</Heading>
        <IntroVideo src="/video/nichotan.mp4" poster="/images/band.webp" label="Nicho Tan Consulting introduction video" />
      </Section>
    </>
  );
}
