# Nicho Tan website (www.nichotan.com)

The public marketing site, rebuilt from the old WordPress/Divi site in Next.js (React) and Tailwind CSS. **Copy and content are unchanged**: the text in `src/content/content.json` was extracted verbatim from the old pages, including its typos ("aproach", "Assesment", "Master Internation Marketing", two services numbered "4."). Fixing those is a separate, later decision.

It is a **static export**: `npm run build` writes plain HTML/CSS/JS to `out/`, which any static host can serve. There is no server, database or admin login.

## Run

```
npm install
npm run dev        # http://localhost:3000
npm run build      # writes out/
npx serve out      # or any static server, to preview the build
```

## Where things are

| What | Where |
| --- | --- |
| Page text | `src/content/content.json` |
| Nav, contact email, Facebook, analytics/chat/ICEF ids, footer | `src/content/site.ts` |
| Pages | `src/app/**/page.tsx` (`/`, `/about/`, `/services/`, `/gallery/`, `/application-requirements/`, `/contact-us/`, and the three university event registration pages via `src/app/[event]/`) |
| Colours | `src/app/globals.css` (same palette as the Nicho Tan app) |
| Images | `public/images`, produced by `scripts/prepare-images.py` from the originals on the old site |
| Video | `public/video/nichotan.mp4` (see below) |

Every page keeps its old WordPress address with a trailing slash, so existing links and Google results keep working. The two old test pages (`team_test`, `gallery_playgroudn`) were not migrated.

## Things that behave differently from the old site

- **Contact form**: Submit sends the message to the Nicho Tan app (`POST /api/enquiries/website`), where it appears in the staff Enquiries list marked "Website message", and the enquiries mailbox is told. The visitor sees a thank-you; if the app can't be reached they get a link to email the message instead, so nothing is lost. Phone numbers must start with a country code (the app's rule), so the form says so. The address is `enquiryEndpoint` in `src/content/site.ts`; to try it against a local API, build with `NEXT_PUBLIC_ENQUIRY_URL=http://localhost:5210/api/enquiries/website` and add the website's local address to the API's `Cors__AllowedOrigins__1`. The API only accepts calls from addresses in its `websiteOrigins` setting (see `docs/deployment.md`), so deploy the API change **before** the website goes live.
- **"Download Application Form"** now goes to `https://app.nichotan.com/register` (the old link pointed at a file on WordPress). Change it in `src/content/site.ts`.
- **Event registration pages** still embed the LeadConnector forms, and the LeadConnector chat widget, Google tag (`GT-NFR3C2DC`) and ICEF badge are carried over.
- **Editing**: content is edited in `content.json` and redeployed. There is no WordPress dashboard any more.
- **Redirects/headers** cannot be set in a static export; configure them on the host if needed.

## Video

The old homepage video is ~86 MB, too heavy to ship. Compress it before deploying:

```
ffmpeg -i nichotan.mp4 -vf scale=-2:720 -c:v libx264 -crf 28 -preset slow -c:a aac -b:a 96k -movflags +faststart public/video/nichotan.mp4
```

It loads only when a visitor presses play (`preload="none"`, with a poster).

## Going live: DNS cutover checklist

1. Deploy `out/` to a static host (e.g. Azure Static Web Apps, free tier) and confirm it works on the host's own address.
2. Add `www.nichotan.com` as a custom domain on that host; it will give you the DNS records to set.
3. Get Utande to change **only** the website records: `www` (CNAME) and the apex `nichotan.com` (redirect to `www`, or A/ALIAS as the host instructs).
4. **Do not touch** MX, SPF/TXT, DKIM or the `app` / `asuid.app` records: email (`info@`, `DoNotReply@`) and `app.nichotan.com` depend on them.
5. Lower the TTL a day beforehand so the switch (and any rollback) is quick. Keep the old WordPress hosting alive until the new site is confirmed everywhere.
6. After the switch, check every page, the contact form, an event registration form, and that the chat widget and analytics load.
