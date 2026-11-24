import type { Metadata } from "next";
import { notFound } from "next/navigation";
import LeadConnectorForm from "@/components/LeadConnectorForm";
import PageHero from "@/components/PageHero";
import Section from "@/components/Section";
import content from "@/content/content.json";

// Only the three event slugs exist; any other address is a 404.
export const dynamicParams = false;

export function generateStaticParams() {
  return content.events.map((e) => ({ event: e.slug }));
}

type Params = { params: Promise<{ event: string }> };

async function find(params: Params["params"]) {
  const { event } = await params;
  return content.events.find((e) => e.slug === event);
}

export async function generateMetadata({ params }: Params): Promise<Metadata> {
  const e = await find(params);
  return e ? { title: e.wpTitle, alternates: { canonical: `/${e.slug}/` } } : {};
}

export default async function EventPage({ params }: Params) {
  const e = await find(params);
  if (!e) notFound();
  return (
    <>
      <PageHero title={e.wpTitle} />
      <Section>
        <div className="mx-auto max-w-3xl">
          <LeadConnectorForm formId={e.formId} name={e.formName} height={e.height} />
        </div>
      </Section>
    </>
  );
}
