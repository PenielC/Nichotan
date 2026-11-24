import Image from "next/image";

/** `dark` logos are white artwork on a transparent background, so they sit on a navy tile. */
const partners: { file: string; name: string; w: number; h: number; dark?: boolean }[] = [
  { file: "partner-1", name: "SEGi University", w: 250, h: 250 },
  { file: "partner-2", name: "Cape Breton University", w: 250, h: 250 },
  { file: "partner-3", name: "Trent University", w: 250, h: 250, dark: true },
  { file: "partner-4", name: "MacEwan University", w: 250, h: 250, dark: true },
  { file: "partner-5", name: "University of Manitoba", w: 250, h: 250 },
  { file: "partner-6", name: "MAHSA University", w: 250, h: 250 },
  { file: "partner-7", name: "Bodwell High School", w: 250, h: 250 },
  { file: "partner-8", name: "College of New Caledonia", w: 250, h: 250 },
  { file: "partner-9", name: "Acadia University", w: 250, h: 250, dark: true },
  { file: "partner-10", name: "Asia Pacific University", w: 250, h: 250 },
  { file: "partner-11", name: "University of New Brunswick", w: 250, h: 250, dark: true },
  { file: "partner-12", name: "Okanagan College", w: 250, h: 250 },
  { file: "partner-13", name: "University of Alberta", w: 250, h: 250 },
  { file: "partner-14", name: "Centennial College", w: 250, h: 250 },
  { file: "partner-15", name: "University of Waterloo", w: 250, h: 250 },
  { file: "partner-16", name: "Douglas College", w: 250, h: 250 },
  { file: "vancouver", name: "Vancouver Island University", w: 250, h: 167 },
  { file: "windsor", name: "University of Windsor", w: 301, h: 167 },
  { file: "download-2", name: "Ontario Tech University", w: 301, h: 167 },
  { file: "simon-fraser", name: "Simon Fraser University", w: 600, h: 400 },
  { file: "northen_b_columbia", name: "University of Northern British Columbia", w: 297, h: 170 },
];

function Logo({ p, dup = false }: { p: (typeof partners)[number]; dup?: boolean }) {
  return (
    <li aria-hidden={dup || undefined} className={`${dup ? "marquee-dup " : ""}flex h-28 w-44 shrink-0 items-center justify-center rounded-xl border border-line p-3 shadow-sm ${p.dark ? "bg-ink" : "bg-surface"}`}>
      <Image src={`/images/partners/${p.file}.webp`} alt={dup ? "" : p.name} width={p.w} height={p.h} loading="eager" className="max-h-full w-auto object-contain" />
    </li>
  );
}

/** A slowly scrolling band of logos (pauses on hover; a static wrapped grid for people who prefer reduced motion). */
export function PartnerMarquee() {
  return (
    <div className="marquee overflow-hidden [mask-image:linear-gradient(to_right,transparent,black_8%,black_92%,transparent)]">
      <ul className="marquee-track flex gap-4" aria-label="Partner institutions">
        {partners.map((p) => <Logo key={p.file} p={p} />)}
        {/* Second copy makes the loop seamless; hidden from assistive tech so each partner is announced once. */}
        {partners.map((p) => <Logo key={`d-${p.file}`} p={p} dup />)}
      </ul>
    </div>
  );
}

export default function PartnerLogos() {
  return (
    <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
      {partners.map((p) => (
        <li key={p.file} className={`flex h-28 items-center justify-center rounded-lg border border-line p-3 ${p.dark ? "bg-ink" : "bg-surface"}`}>
          <Image src={`/images/partners/${p.file}.webp`} alt={p.name} width={p.w} height={p.h} loading="lazy" className="max-h-full w-auto object-contain" />
        </li>
      ))}
    </ul>
  );
}
