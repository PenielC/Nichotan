import Image from "next/image";

const certificates = [
  { src: "/images/icef-accreditation.webp", w: 1000, h: 707, alt: "ICEF accredited education agency certificate" },
  { src: "/images/borderpass-certificate.webp", w: 1400, h: 853, alt: "Borderpass verified agent certificate" },
  { src: "/images/british-council-certificate.webp", w: 634, h: 900, alt: "British Council certificate for UK education agent training, Bright Jonas" },
];

/** Each certificate opens full size in a new tab, so the small print stays readable. */
export default function Certificates() {
  return (
    <ul className="grid gap-6 md:grid-cols-3">
      {certificates.map((c) => (
        <li key={c.src}>
          <a
            href={c.src}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`${c.alt} (opens full size in a new tab)`}
            className="group flex h-80 items-center justify-center overflow-hidden rounded-2xl bg-surface p-5 shadow-md ring-1 ring-line transition hover:-translate-y-1 hover:shadow-xl"
          >
            <Image src={c.src} alt="" width={c.w} height={c.h} loading="lazy" className="max-h-full w-auto max-w-full object-contain transition duration-500 group-hover:scale-105" />
          </a>
        </li>
      ))}
    </ul>
  );
}
