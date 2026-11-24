import type { NextConfig } from "next";

/**
 * The public marketing site: plain static pages, no server. `next build` writes ready-to-serve files to `out/`, which any static host can serve.
 * (Cache Components and partial prefetching, which the scaffold enables, are for apps that mix live data with static pages; this site has no live data.)
 */
const nextConfig: NextConfig = {
  output: "export",
  // Every page keeps its old WordPress address with the trailing slash (/about/, /contact-us/), so existing links and search results keep working.
  trailingSlash: true,
  // A static export cannot use Next's image optimiser. Images are resized and converted once, ahead of time, by scripts/prepare-images.py.
  images: { unoptimized: true },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
