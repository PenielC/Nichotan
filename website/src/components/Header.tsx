"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, useSyncExternalStore } from "react";
import { site } from "@/content/site";

// True once the page has scrolled away from the very top. Read straight from the browser so there is no flash or extra render pass.
function subscribeToScroll(notify: () => void) {
  window.addEventListener("scroll", notify, { passive: true });
  return () => window.removeEventListener("scroll", notify);
}
const scrolledNow = () => window.scrollY > 8;

export default function Header() {
  const path = usePathname();
  const [open, setOpen] = useState(false);
  const scrolled = useSyncExternalStore(subscribeToScroll, scrolledNow, () => false);
  // Transparent over the hero at the top of the page; solid navy once scrolled, or while the mobile menu is open (so its links stay readable).
  const solid = scrolled || open;

  return (
    <header className={`fixed inset-x-0 top-0 z-40 text-white transition-colors duration-300 ${solid ? "bg-ink shadow-md" : "bg-gradient-to-b from-ink/70 to-transparent"}`}>
      <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-3">
        <Link href="/" className="flex items-center" aria-label="Nicho Tan Consulting home" onClick={() => setOpen(false)}>
          <Image src="/images/logo.webp" alt="Nicho Tan Consulting" width={449} height={520} className={`w-auto transition-all duration-300 ${scrolled ? "h-16 md:h-[4.5rem]" : "h-20 md:h-28"}`} priority />
        </Link>
        <button
          type="button"
          className="rounded-md border border-white/30 px-3 py-2 text-sm font-medium md:hidden"
          aria-expanded={open}
          aria-controls="main-nav"
          onClick={() => setOpen((v) => !v)}
        >
          {open ? "Close" : "Menu"}
        </button>
        <nav
          id="main-nav"
          aria-label="Main"
          className={`${open ? "block" : "hidden"} absolute left-0 right-0 top-full bg-ink px-4 pb-4 md:static md:block md:bg-transparent md:p-0`}
        >
          <ul className="[text-shadow:0_1px_8px_rgba(0,0,0,0.7)] flex flex-col gap-1 md:flex-row md:gap-1">
            {site.nav.map((item) => {
              const active = item.href === "/" ? path === "/" : path.startsWith(item.href);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    onClick={() => setOpen(false)}
                    aria-current={active ? "page" : undefined}
                    className={`block rounded-md px-3 py-2 text-sm font-medium hover:bg-white/10 ${active ? "text-leaf" : "text-white"}`}
                  >
                    {item.label}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>
      </div>
    </header>
  );
}
