"use client";

import { useRef, useState } from "react";

type T = { author: string; role: string; quote: string };

/** A row of testimonials that scrolls sideways: swipe or drag on a phone, use the arrows, or focus the row and use the arrow keys. */
export default function Testimonials({ items }: { items: T[] }) {
  const row = useRef<HTMLUListElement>(null);
  const [edge, setEdge] = useState({ start: true, end: false });

  function update() {
    const el = row.current;
    if (!el) return;
    setEdge({ start: el.scrollLeft <= 4, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 4 });
  }

  function scrollByCard(dir: 1 | -1) {
    const el = row.current;
    if (!el) return;
    const card = el.querySelector("li");
    const step = (card?.getBoundingClientRect().width ?? el.clientWidth) + 24;
    el.scrollBy({ left: dir * step, behavior: "smooth" });
  }

  const arrow = "grid h-11 w-11 place-items-center rounded-full border border-line bg-surface text-xl text-ink shadow-sm hover:border-forest disabled:opacity-40 disabled:hover:border-line";

  return (
    <div>
      <div className="mb-5 flex justify-end gap-2">
        <button type="button" aria-label="Previous testimonials" disabled={edge.start} onClick={() => scrollByCard(-1)} className={arrow}>
          <span aria-hidden>‹</span>
        </button>
        <button type="button" aria-label="More testimonials" disabled={edge.end} onClick={() => scrollByCard(1)} className={arrow}>
          <span aria-hidden>›</span>
        </button>
      </div>
      <ul
        ref={row}
        onScroll={update}
        tabIndex={0}
        aria-label="Testimonials, scroll sideways"
        className="-mx-4 flex snap-x snap-mandatory gap-6 overflow-x-auto scroll-smooth px-4 pb-6 [scrollbar-color:var(--leaf)_transparent] [scrollbar-width:thin]"
      >
        {items.map((t) => (
          <li key={t.author} className="flex w-[86%] shrink-0 snap-start sm:w-[26rem]">
            <figure className="relative flex w-full flex-col overflow-hidden rounded-2xl border border-line bg-surface p-7 shadow-sm transition hover:shadow-lg">
              <span aria-hidden className="pointer-events-none absolute -top-4 right-4 select-none font-serif text-9xl leading-none text-leaf/30">&rdquo;</span>
              <blockquote className="relative flex-1 text-base leading-relaxed text-ink">{t.quote}</blockquote>
              <figcaption className="mt-5 flex min-h-[6.25rem] items-start gap-3 border-t border-line pt-4">
                <span aria-hidden className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-ink text-sm font-bold text-leaf">{t.author.trim()[0]}</span>
                <span>
                  <span className="block font-semibold text-ink">{t.author}</span>
                  <span className="block text-sm text-muted">{t.role}</span>
                </span>
              </figcaption>
            </figure>
          </li>
        ))}
      </ul>
    </div>
  );
}
