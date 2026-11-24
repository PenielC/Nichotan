import type { Metadata } from "next";
import { Geist } from "next/font/google";
import Script from "next/script";
import Footer from "@/components/Footer";
import Header from "@/components/Header";
import { site } from "@/content/site";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });

export const metadata: Metadata = {
  metadataBase: new URL(site.url),
  title: { default: site.name, template: `%s | ${site.name}` },
  description: site.description,
  openGraph: {
    siteName: site.name,
    type: "website",
    images: [{ url: "/images/og.jpg", width: 1200, height: 630 }],
  },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${geistSans.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col">
        <a href="#content" className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-white focus:px-3 focus:py-2 focus:text-ink">
          Skip to content
        </a>
        <Header />
        <main id="content" className="flex-1">{children}</main>
        <Footer />
        <Script src={`https://www.googletagmanager.com/gtag/js?id=${site.googleTag}`} strategy="afterInteractive" />
        <Script id="gtag" strategy="afterInteractive">
          {`window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}gtag('js',new Date());gtag('config','${site.googleTag}');`}
        </Script>
        <Script
          src="https://widgets.leadconnectorhq.com/loader.js"
          data-resources-url="https://widgets.leadconnectorhq.com/chat-widget/loader.js"
          data-widget-id={site.chatWidgetId}
          strategy="lazyOnload"
        />
      </body>
    </html>
  );
}
