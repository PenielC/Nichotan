import type { Metadata } from "next";
import Image from "next/image";
import PageHero from "@/components/PageHero";
import Section from "@/components/Section";
import content from "@/content/content.json";

const services = content.services;

export const metadata: Metadata = { title: services.title, alternates: { canonical: "/services/" } };

export default function Services() {
  return (
    <>
      <PageHero title={services.title} />
      <Section>
        <div className="space-y-12">
          {services.items.map((s, i) => (
            <article key={s.heading} className="grid items-center gap-6 md:grid-cols-2">
              <Image
                src={`/images/service-${i + 1}.webp`}
                alt="Nicho Tan students and counsellors at a seminar or event"
                width={1000}
                height={667}
                loading="lazy"
                className={`w-full rounded-xl ${i % 2 ? "md:order-2" : ""}`}
              />
              <div>
                <h2 className="mb-3 text-2xl font-bold text-ink">{s.heading}</h2>
                <div className="space-y-3 text-lg leading-relaxed text-muted">
                  {s.paragraphs.map((p) => <p key={p}>{p}</p>)}
                </div>
              </div>
            </article>
          ))}
        </div>
      </Section>
    </>
  );
}
