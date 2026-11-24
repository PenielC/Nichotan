import type { Metadata } from "next";
import Gallery from "@/components/Gallery";
import PageHero from "@/components/PageHero";
import Section from "@/components/Section";
import content from "@/content/content.json";

export const metadata: Metadata = { title: content.gallery.title, alternates: { canonical: "/gallery/" } };

const count = 18;
const photos = Array.from({ length: count }, (_, i) => {
  const n = String(i + 1).padStart(2, "0");
  return { thumb: `/images/gallery/${n}-thumb.webp`, full: `/images/gallery/${n}.webp`, alt: `Nicho Tan students, graduates and team: photo ${i + 1} of ${count}` };
});

export default function GalleryPage() {
  return (
    <>
      <PageHero title={content.gallery.title} />
      <Section>
        <Gallery photos={photos} />
      </Section>
    </>
  );
}
