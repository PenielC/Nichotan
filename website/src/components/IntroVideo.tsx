"use client";

import { useEffect, useRef } from "react";

/**
 * Starts playing, muted, the first time at least half of the video is on screen, and pauses when it scrolls away.
 * Browsers only allow autoplay when muted, so the sound is one tap away on the video's own controls.
 * It does not autoplay for visitors who ask their device to reduce motion.
 */
export default function IntroVideo({ src, poster, label }: { src: string; poster: string; label: string }) {
  const video = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    const el = video.current;
    if (!el || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    let started = false;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && !started) {
          started = true;
          el.muted = true;
          el.play().catch(() => {}); // blocked by the browser: the visitor can still press play
        } else if (!entry.isIntersecting && !el.paused) {
          el.pause();
        }
      },
      { threshold: 0.5 },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <video
      ref={video}
      controls
      muted
      playsInline
      preload="none"
      poster={poster}
      aria-label={label}
      className="mx-auto w-full max-w-4xl rounded-2xl bg-black shadow-2xl ring-4 ring-leaf/40"
    >
      <source src={src} type="video/mp4" />
    </video>
  );
}
