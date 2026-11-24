import type { ReactNode } from "react";

export default function Section({ children, tone = "plain", className = "" }: { children: ReactNode; tone?: "plain" | "white" | "wash" | "navy"; className?: string }) {
  const bg = tone === "white" ? "bg-surface" : tone === "wash" ? "bg-wash" : tone === "navy" ? "bg-ink text-white" : "";
  return (
    <section className={`${bg} ${className}`}>
      <div className="mx-auto max-w-6xl px-4 py-14 md:py-20">{children}</div>
    </section>
  );
}

/** Section title with a short green-to-lime bar under it. */
export function Heading({ children, center = false, light = false }: { children: ReactNode; center?: boolean; light?: boolean }) {
  return (
    <div className={`mb-10 ${center ? "text-center" : ""}`}>
      <h2 className={`text-3xl font-bold tracking-tight md:text-4xl ${light ? "text-white" : "text-ink"}`}>{children}</h2>
      <span aria-hidden className={`mt-3 block h-1.5 w-16 rounded-full bg-gradient-to-r from-forest to-leaf ${center ? "mx-auto" : ""}`} />
    </div>
  );
}
