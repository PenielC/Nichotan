import type { Metadata } from "next";
import PageHero from "@/components/PageHero";
import Section, { Heading } from "@/components/Section";
import content from "@/content/content.json";
import { site } from "@/content/site";

const r = content.requirements;

export const metadata: Metadata = { title: r.title, alternates: { canonical: "/application-requirements/" } };

function List({ items }: { items: string[] }) {
  return (
    <ul className="space-y-3">
      {items.map((t) => (
        <li key={t} className="flex gap-3 rounded-lg border border-line bg-surface p-4">
          <span aria-hidden className="mt-1 h-3 w-3 shrink-0 rounded-full bg-leaf" />
          <span className="text-ink">{t}</span>
        </li>
      ))}
    </ul>
  );
}

export default function Requirements() {
  return (
    <>
      <PageHero title={r.title} />
      <Section>
        <div className="grid gap-10 md:grid-cols-2">
          <div>
            <Heading>{r.applicationHeading}</Heading>
            <List items={r.applicationItems} />
          </div>
          <div>
            <Heading>{r.graduateHeading}</Heading>
            <List items={r.graduateItems} />
          </div>
        </div>
        <a
          href={site.applicationFormUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="mt-10 inline-block rounded-md bg-forest px-6 py-3 font-semibold text-on-forest hover:brightness-110"
        >
          {r.button}
          <span className="sr-only"> (opens in a new tab)</span>
        </a>
      </Section>
    </>
  );
}
