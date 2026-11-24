import Image from "next/image";

/**
 * The partner list, in the order it is shown.
 * `dark` logos have white artwork, so they sit on a navy tile. A partner with no `file` shows its name on a tile until its logo is added
 * (put the image in public/images/partners and set `file`, plus `ext` if it is not a .webp).
 */
type Partner = { name: string; file?: string; ext?: "webp" | "svg"; w?: number; h?: number; dark?: boolean };

const partners: Partner[] = [
  { name: "University of Manitoba", file: "partner-5", w: 250, h: 250 },
  { name: "University of Northern British Columbia", file: "northen_b_columbia", w: 297, h: 170 },
  { name: "Trent University", file: "partner-3", w: 250, h: 250, dark: true },
  { name: "Ontario Tech University", file: "download-2", w: 301, h: 167 },
  { name: "University of Windsor", file: "windsor", w: 301, h: 167 },
  { name: "Simon Fraser University", file: "simon-fraser", w: 600, h: 400 },
  { name: "Thompson Rivers University", file: "thompson-rivers", w: 500, h: 185 },
  { name: "Kwantlen Polytechnic University", file: "kpu", ext: "svg", w: 110, h: 101 },
  { name: "North Island College", file: "north-island", ext: "svg", w: 303, h: 123, dark: true },
  { name: "Saskatchewan Polytechnic", file: "sask-polytechnic", w: 300, h: 155 },
  { name: "Northern Lights College", file: "northern-lights-college", ext: "svg", w: 260, h: 54 },
  { name: "College of New Caledonia", file: "partner-8", w: 250, h: 250 },
  { name: "Centennial College", file: "partner-14", w: 250, h: 250 },
  { name: "Alexander College", file: "alexander", w: 500, h: 101, dark: true },
  { name: "Columbia College", file: "columbia", ext: "svg", w: 196, h: 61 },
  { name: "Huron University", file: "huron", ext: "svg", w: 612, h: 180 },
  { name: "IU (International University of Applied Sciences) – Germany", file: "iu", ext: "svg", w: 426, h: 142 },
  { name: "SRH University – Germany", file: "srh", w: 500, h: 111 },
  { name: "University of Europe – Germany", file: "university-of-europe", ext: "svg", w: 228, h: 64, dark: true },
  { name: "SEGi University – Malaysia", file: "partner-1", w: 250, h: 250 },
  { name: "Cape Breton University", file: "partner-2", w: 250, h: 250 },
  { name: "MAHSA University", file: "partner-6", w: 250, h: 250 },
  { name: "Bodwell High School", file: "partner-7", w: 250, h: 250 },
  { name: "Asia Pacific University", file: "partner-10", w: 250, h: 250 },
  { name: "Okanagan College", file: "partner-12", w: 250, h: 250 },
  { name: "University of Alberta", file: "partner-13", w: 250, h: 250 },
  { name: "Douglas College", file: "partner-16", w: 250, h: 250 },
  { name: "Vancouver Island University", file: "vancouver", w: 250, h: 167 },
  { name: "Arden University – UK", file: "arden", ext: "svg", w: 88, h: 104 },
  { name: "Westcliff University – USA", file: "westcliff", w: 300, h: 75, dark: true },
  { name: "Golden Hills High School", file: "golden-hills-high-school", w: 500, h: 130 },
];

function Logo({ p, dup = false }: { p: Partner; dup?: boolean }) {
  return (
    <li aria-hidden={dup || undefined} className={`${dup ? "marquee-dup " : ""}flex h-28 w-44 shrink-0 items-center justify-center rounded-xl border border-line p-3 shadow-sm ${p.dark ? "bg-ink" : "bg-surface"}`}>
      {p.file ? (
        <Image src={`/images/partners/${p.file}.${p.ext ?? "webp"}`} alt={dup ? "" : p.name} width={p.w} height={p.h} loading="eager" className="max-h-full max-w-full w-auto object-contain" />
      ) : (
        <span className="text-center text-sm font-semibold leading-snug text-ink">{p.name}</span>
      )}
    </li>
  );
}

/** A slowly scrolling band of logos (pauses on hover; a static wrapped grid for people who prefer reduced motion). */
export function PartnerMarquee() {
  return (
    <div className="marquee overflow-hidden [mask-image:linear-gradient(to_right,transparent,black_8%,black_92%,transparent)]">
      <ul className="marquee-track flex gap-4" aria-label="Partner institutions">
        {partners.map((p) => <Logo key={p.name} p={p} />)}
        {/* Second copy makes the loop seamless; hidden from assistive tech so each partner is announced once. */}
        {partners.map((p) => <Logo key={`d-${p.name}`} p={p} dup />)}
      </ul>
    </div>
  );
}

export default function PartnerLogos() {
  return (
    <ul className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6">
      {partners.map((p) => (
        <li key={p.name} className={`flex h-28 items-center justify-center rounded-lg border border-line p-3 ${p.dark ? "bg-ink" : "bg-surface"}`}>
          {p.file ? (
            <Image src={`/images/partners/${p.file}.${p.ext ?? "webp"}`} alt={p.name} width={p.w} height={p.h} loading="lazy" className="max-h-full max-w-full w-auto object-contain" />
          ) : (
            <span className="text-center text-sm font-semibold leading-snug text-ink">{p.name}</span>
          )}
        </li>
      ))}
    </ul>
  );
}
