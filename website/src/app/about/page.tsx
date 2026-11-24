import type { Metadata } from "next";
import Image from "next/image";
import PageHero from "@/components/PageHero";
import PartnerLogos from "@/components/PartnerLogos";
import Section, { Heading } from "@/components/Section";
import content from "@/content/content.json";

const about = content.about;

export const metadata: Metadata = { title: about.title, alternates: { canonical: "/about/" } };

export default function About() {
  return (
    <>
      <PageHero title={about.title} />
      <Section>
        <Heading>{about.story.heading}</Heading>
        <div className="max-w-3xl space-y-4 text-lg leading-relaxed text-muted">
          {about.story.paragraphs.map((p) => <p key={p}>{p}</p>)}
        </div>
      </Section>
      <Section tone="wash">
        <Heading>{about.missionVision.heading}</Heading>
        <div className="grid gap-6 md:grid-cols-2">
          {about.missionVision.blurbs.map((b) => (
            <div key={b.title} className="rounded-xl bg-surface p-6 shadow-sm">
              <h3 className="mb-3 text-xl font-semibold text-forest">{b.title}</h3>
              {b.paragraphs.map((p) => <p key={p} className="text-muted">{p}</p>)}
            </div>
          ))}
        </div>
      </Section>
      <Section>
        <Heading>{about.leadership.heading}</Heading>
        <div className="grid gap-8 md:grid-cols-[1fr_auto] md:items-start">
          <div className="space-y-4 text-lg leading-relaxed text-muted">
            {about.leadership.paragraphs.map((p) => <p key={p}>{p}</p>)}
          </div>
          <div className="flex gap-4">
            <figure>
              <Image src="/images/chingombe.webp" alt="Doctor Purazen Chingombe, CEO" width={212} height={300} className="rounded-lg" />
              <figcaption className="mt-2 text-center text-sm text-muted">Doctor Purazen Chingombe</figcaption>
            </figure>
            <figure>
              <Image src="/images/jonas.webp" alt="Bright Jonas, Director" width={212} height={300} className="rounded-lg" />
              <figcaption className="mt-2 text-center text-sm text-muted">Bright Jonas</figcaption>
            </figure>
          </div>
        </div>
      </Section>
      <Section tone="white">
        <Heading>{about.team.heading}</Heading>
        {about.team.paragraphs.map((p) => <p key={p} className="max-w-3xl text-lg text-muted">{p}</p>)}
        <Image src="/images/home-4.webp" alt="The Nicho Tan team in branded polo shirts" width={1000} height={750} loading="lazy" className="mt-6 w-full max-w-3xl rounded-xl" />
      </Section>
      <Section>
        <Heading>{about.partners.heading}</Heading>
        {about.partners.paragraphs.map((p) => <p key={p} className="mb-8 max-w-3xl text-lg text-muted">{p}</p>)}
        <PartnerLogos />
      </Section>
      <Section tone="white">
        <h2 className="sr-only">Accreditations</h2>
        <div className="grid gap-6 md:grid-cols-3 md:items-center">
          <Image src="/images/icef-accreditation.webp" alt="ICEF accredited education agency" width={1000} height={707} loading="lazy" className="w-full rounded-lg" />
          <Image src="/images/borderpass-certificate.webp" alt="Borderpass verified agent certificate" width={1400} height={853} loading="lazy" className="w-full rounded-lg" />
          <Image src="/images/british-council-certificate.webp" alt="British Council certificate for UK education agent training, Bright Jonas" width={634} height={900} loading="lazy" className="w-full rounded-lg" />
        </div>
      </Section>
    </>
  );
}
