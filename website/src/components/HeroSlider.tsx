"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useState } from "react";
import { internal } from "@/content/site";

type Slide = { text: string; src: string; alt: string; buttons: { label: string; href: string }[] };

const INTERVAL = 5000;

export default function HeroSlider({ slides }: { slides: Slide[] }) {
  const [index, setIndex] = useState(0);
  // Only an explicit press of the pause button stops it, so the slides keep moving while the pointer is over them.
  const [paused, setPaused] = useState(false);

  useEffect(() => {
    if (paused || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    // A timeout (not an interval) restarts the 5 seconds whenever someone picks a slide by hand.
    const t = setTimeout(() => setIndex((i) => (i + 1) % slides.length), INTERVAL);
    return () => clearTimeout(t);
  }, [paused, index, slides.length]);

  const go = (n: number) => setIndex((n + slides.length) % slides.length);

  return (
    <section className="relative isolate min-h-[34rem] overflow-hidden bg-ink text-white md:min-h-[42rem]" aria-roledescription="carousel" aria-label="Nicho Tan highlights">
      {slides.map((s, i) => (
        <div
          key={i}
          role="group"
          aria-roledescription="slide"
          aria-label={`${i + 1} of ${slides.length}`}
          aria-hidden={i !== index}
          className={`absolute inset-0 transition-opacity duration-1000 ${i === index ? "opacity-100" : "pointer-events-none opacity-0"}`}
        >
          <Image
            src={s.src}
            alt={s.alt}
            fill
            sizes="100vw"
            priority={i === 0}
            className={`-z-20 object-cover ${i === index ? "hero-zoom" : ""}`}
          />
          <div className="absolute inset-0 -z-10 bg-gradient-to-t from-ink via-ink/55 to-ink/30" />
          <div className="absolute inset-0 -z-10 bg-gradient-to-r from-ink/70 via-transparent to-transparent" />
          <div className="mx-auto flex h-full max-w-6xl flex-col justify-end gap-7 px-6 pb-28 pt-24 md:justify-center md:pb-16 lg:pb-32">
            <span className="h-1.5 w-16 rounded-full bg-leaf" aria-hidden />
            <p className={`max-w-2xl text-2xl font-medium leading-snug md:text-[30px] md:leading-snug ${i === index ? "hero-rise" : ""}`}>{s.text}</p>
            <div className={`flex flex-wrap gap-3 ${i === index ? "hero-rise-late" : ""}`}>
              {s.buttons.map((b, n) => (
                <Link
                  key={b.label}
                  href={internal(b.href)}
                  tabIndex={i === index ? 0 : -1}
                  className={`rounded-full px-7 py-3 text-sm font-semibold transition ${n === 0 ? "bg-leaf text-ink shadow-lg shadow-black/30 hover:-translate-y-0.5 hover:brightness-110" : "border-2 border-white/80 hover:bg-white hover:text-ink"}`}
                >
                  {b.label}
                </Link>
              ))}
            </div>
          </div>
        </div>
      ))}

      {/* Controls */}
      <div className="absolute inset-x-0 bottom-6 lg:bottom-24 mx-auto flex max-w-6xl items-center gap-4 px-6">
        <button type="button" aria-label="Previous slide" onClick={() => go(index - 1)} className="grid h-10 w-10 place-items-center rounded-full border border-white/50 hover:bg-white/15">
          <span aria-hidden>‹</span>
        </button>
        <button type="button" aria-label="Next slide" onClick={() => go(index + 1)} className="grid h-10 w-10 place-items-center rounded-full border border-white/50 hover:bg-white/15">
          <span aria-hidden>›</span>
        </button>
        <button type="button" aria-label={paused ? "Resume automatic slide change" : "Pause automatic slide change"} onClick={() => setPaused((p) => !p)} className="grid h-10 w-10 place-items-center rounded-full border border-white/50 text-xs hover:bg-white/15">
          <span aria-hidden>{paused ? "▶" : "❚❚"}</span>
        </button>
        <div className="flex flex-1 gap-2">
          {slides.map((_, i) => (
            <button key={i} type="button" aria-label={`Show slide ${i + 1}`} aria-current={i === index} onClick={() => go(i)} className="group h-6 flex-1 max-w-24">
              <span className="block h-1 overflow-hidden rounded-full bg-white/30">
                <span
                  key={i === index ? `on-${index}` : "off"}
                  className={`block h-full bg-leaf ${i < index ? "w-full" : i === index ? "hero-progress" : "w-0"}`}
                  style={i === index ? { animationPlayState: paused ? "paused" : "running" } : undefined}
                />
              </span>
            </button>
          ))}
        </div>
      </div>
    </section>
  );
}
