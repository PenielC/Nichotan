import Image from "next/image";
import Link from "next/link";
import content from "@/content/content.json";
import { site } from "@/content/site";

const contact = Object.fromEntries(content.contact.details.map((d) => [d.title, d.text]));

function FooterHeading({ children }: { children: string }) {
  return (
    <h2 className="mb-4 text-sm font-semibold uppercase tracking-wider text-leaf">{children}</h2>
  );
}

export default function Footer() {
  return (
    <footer className="relative isolate overflow-hidden bg-ink text-white">
      <Image src="/images/footer-bg.webp" alt="" fill sizes="100vw" loading="lazy" className="-z-20 object-cover opacity-15" />
      <div aria-hidden className="absolute inset-0 -z-10 bg-gradient-to-b from-ink via-ink/90 to-ink" />
      <div aria-hidden className="h-1.5 bg-gradient-to-r from-forest via-leaf to-forest" />

      <div className="mx-auto grid max-w-6xl gap-10 px-4 py-14 sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <Link href="/" aria-label="Nicho Tan Consulting home">
            <Image src="/images/logo.webp" alt="Nicho Tan Consulting" width={449} height={520} loading="lazy" className="h-28 w-auto" />
          </Link>
          <a
            href={site.facebook}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="Nicho Tan on Facebook (opens in a new tab)"
            className="mt-6 inline-grid h-11 w-11 place-items-center rounded-full bg-white/10 text-white transition hover:-translate-y-0.5 hover:bg-leaf hover:text-ink"
          >
            <svg viewBox="0 0 24 24" aria-hidden className="h-5 w-5" fill="currentColor">
              <path d="M13.5 22v-8.2h2.8l.5-3.3h-3.3V8.4c0-.9.4-1.7 1.8-1.7h1.6V3.8s-1.3-.2-2.6-.2c-2.6 0-4.3 1.6-4.3 4.4v2.5H7.2v3.3H10V22h3.5Z" />
            </svg>
          </a>
        </div>

        <nav aria-label="Footer">
          <FooterHeading>Quick links</FooterHeading>
          <ul className="space-y-2.5">
            {site.nav.map((n) => (
              <li key={n.href}>
                <Link href={n.href} className="text-white/80 transition hover:text-leaf">{n.label}</Link>
              </li>
            ))}
          </ul>
        </nav>

        <div>
          <FooterHeading>Contact</FooterHeading>
          <dl className="space-y-4 text-white/80">
            {content.contact.details.map((d) => (
              <div key={d.title}>
                <dt className="text-sm font-semibold text-white">{d.title}</dt>
                <dd>
                  {d.title === "Email Address" ? (
                    <a href={`mailto:${contact["Email Address"]}`} className="transition hover:text-leaf">{d.text}</a>
                  ) : (
                    d.text
                  )}
                </dd>
              </div>
            ))}
          </dl>
        </div>

        <div>
          <FooterHeading>Accredited by</FooterHeading>
          {/* The ICEF badge image and link, placed directly (the old site loaded them with ICEF's script, which blocked or slow connections can fail to run). */}
          <a
            href={site.icefAgencyUrl}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="ICEF accredited agency (opens in a new tab)"
            className="inline-block rounded-2xl bg-white p-4 shadow-lg transition hover:-translate-y-0.5 hover:shadow-xl"
          >
            <Image src={site.icefBadgeImage} alt="" width={120} height={132} unoptimized loading="lazy" className="h-auto w-[120px]" />
          </a>
        </div>
      </div>

      <div className="border-t border-white/10">
        <p className="mx-auto max-w-6xl px-4 py-5 text-center text-sm text-white/70">{site.footer}</p>
      </div>
    </footer>
  );
}
