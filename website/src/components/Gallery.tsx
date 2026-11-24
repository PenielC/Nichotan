"use client";

import Image from "next/image";
import { useEffect, useRef, useState } from "react";

type Photo = { thumb: string; full: string; alt: string };

export default function Gallery({ photos }: { photos: Photo[] }) {
  const [open, setOpen] = useState<number | null>(null);
  const dialog = useRef<HTMLDialogElement>(null);
  const opener = useRef<HTMLElement | null>(null);

  useEffect(() => {
    const d = dialog.current;
    if (!d) return;
    if (open !== null && !d.open) d.showModal();
    if (open === null && d.open) d.close();
  }, [open]);

  function show(i: number, el: HTMLElement) {
    opener.current = el;
    setOpen(i);
  }
  function close() {
    setOpen(null);
    opener.current?.focus();
  }
  const step = (d: number) => setOpen((i) => (i === null ? i : (i + d + photos.length) % photos.length));

  return (
    <>
      <ul className="grid grid-cols-2 gap-3 md:grid-cols-3">
        {photos.map((p, i) => (
          <li key={p.thumb}>
            <button type="button" className="block w-full overflow-hidden rounded-lg" onClick={(e) => show(i, e.currentTarget)} aria-label={`Enlarge photo ${i + 1} of ${photos.length}`}>
              <Image src={p.thumb} alt={p.alt} width={640} height={480} loading="lazy" className="aspect-[4/3] w-full object-cover transition hover:scale-105" />
            </button>
          </li>
        ))}
      </ul>
      <dialog
        ref={dialog}
        onClose={() => setOpen(null)}
        onClick={(e) => e.target === dialog.current && close()}
        onKeyDown={(e) => {
          if (e.key === "ArrowRight") step(1);
          if (e.key === "ArrowLeft") step(-1);
        }}
        aria-label="Photo viewer"
        className="m-auto max-h-[95vh] max-w-[95vw] bg-transparent p-0 backdrop:bg-black/80"
      >
        {open !== null && (
          <div className="flex flex-col items-center gap-3">
            <Image src={photos[open].full} alt={photos[open].alt} width={1600} height={1067} className="max-h-[80vh] w-auto rounded-lg object-contain" />
            <div className="flex gap-2">
              <button type="button" onClick={() => step(-1)} className="rounded-md bg-white px-4 py-2 text-sm font-medium text-ink">Previous</button>
              <button type="button" onClick={close} autoFocus className="rounded-md bg-leaf px-4 py-2 text-sm font-medium text-ink">Close</button>
              <button type="button" onClick={() => step(1)} className="rounded-md bg-white px-4 py-2 text-sm font-medium text-ink">Next</button>
            </div>
          </div>
        )}
      </dialog>
    </>
  );
}
