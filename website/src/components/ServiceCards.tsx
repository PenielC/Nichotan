import Image from "next/image";

type Item = { title: string; body: string[] };

/** One card per service: the photo, the title and the text, all visible at once (no clicking to reveal). */
export default function ServiceCards({ items, images }: { items: Item[]; images: { src: string; alt: string }[] }) {
  return (
    <ul className="grid gap-6 md:grid-cols-2 lg:grid-cols-6">
      {items.map((it, i) => (
        <li key={it.title} className={`group overflow-hidden rounded-2xl bg-surface shadow-md ring-1 ring-line transition hover:-translate-y-1 hover:shadow-xl ${i < 3 ? "lg:col-span-2" : "lg:col-span-3"}`}>
          <div className="relative aspect-[16/10] overflow-hidden">
            <Image src={images[i].src} alt={images[i].alt} fill sizes="(min-width:1024px) 33vw, 100vw" loading="lazy" className="object-cover transition duration-500 group-hover:scale-105" />
            <div className="absolute inset-x-0 bottom-0 h-1.5 bg-gradient-to-r from-forest to-leaf" />
          </div>
          <div className="p-6">
            <h3 className="mb-2 text-lg font-bold text-ink">{it.title}</h3>
            {it.body.map((p) => (
              <p key={p} className="leading-relaxed text-muted">{p}</p>
            ))}
          </div>
        </li>
      ))}
    </ul>
  );
}
