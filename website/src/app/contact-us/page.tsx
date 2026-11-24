import type { Metadata } from "next";
import ContactForm from "@/components/ContactForm";
import PageHero from "@/components/PageHero";
import Section, { Heading } from "@/components/Section";
import content from "@/content/content.json";
import { site } from "@/content/site";

const c = content.contact;

export const metadata: Metadata = { title: c.title, alternates: { canonical: "/contact-us/" } };

export default function Contact() {
  return (
    <>
      <PageHero title={c.title} />
      <Section>
        <div className="grid gap-12 md:grid-cols-2">
          <div>
            <Heading>{c.formHeading}</Heading>
            <ContactForm fields={c.fields} />
          </div>
          <div>
            <Heading>{c.detailsHeading}</Heading>
            <dl className="space-y-4">
              {c.details.map((d) => (
                <div key={d.title}>
                  <dt className="font-semibold text-ink">{d.title}</dt>
                  <dd className="text-muted">
                    {d.title === "Email Address" ? <a className="underline" href={`mailto:${d.text}`}>{d.text}</a> : d.text}
                  </dd>
                </div>
              ))}
            </dl>
            <iframe
              src={site.mapEmbed}
              title="Map showing the Nicho Tan Consulting office in Milton Park, Harare"
              loading="lazy"
              referrerPolicy="no-referrer-when-downgrade"
              className="mt-8 h-72 w-full rounded-xl border border-line"
            />
          </div>
        </div>
      </Section>
    </>
  );
}
