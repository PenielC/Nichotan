import type { MetadataRoute } from "next";
import content from "@/content/content.json";
import { site } from "@/content/site";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  const paths = [...site.nav.map((n) => n.href), ...content.events.map((e) => `/${e.slug}/`)];
  return paths.map((p) => ({ url: `${site.url}${p}` }));
}
