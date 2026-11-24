"use client";

import { useState } from "react";
import PartnerLogos, { PartnerMarquee } from "@/components/PartnerLogos";

/** Partners scroll past slowly by default; the switch lays all of them out in a grid so every logo can be seen at once. */
export default function PartnerShowcase() {
  const [showAll, setShowAll] = useState(false);

  return (
    <>
      <div className="mb-8 flex justify-center">
        <button
          type="button"
          role="switch"
          aria-checked={showAll}
          onClick={() => setShowAll((v) => !v)}
          className="group inline-flex items-center gap-3 rounded-full border border-line bg-surface px-5 py-2.5 text-sm font-semibold text-ink shadow-sm hover:border-forest"
        >
          <span aria-hidden className={`relative h-6 w-11 rounded-full transition-colors ${showAll ? "bg-forest" : "bg-line"}`}>
            <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all ${showAll ? "left-[1.375rem]" : "left-0.5"}`} />
          </span>
          Show all partners
        </button>
      </div>
      {showAll ? (
        <div className="mx-auto max-w-6xl px-4">
          <PartnerLogos />
        </div>
      ) : (
        <PartnerMarquee />
      )}
    </>
  );
}
