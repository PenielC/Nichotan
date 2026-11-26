/** Site-wide facts that are not page copy. The words themselves live in content.json, copied unchanged from the old site. */
export const site = {
  name: "Nicho Tan Consulting",
  url: "https://www.nichotan.com",
  description:
    "Nicho Tan Consulting was established in 2014 in Zimbabwe to provide consultancy services to students who aspire to study abroad, expand their academic horizons, career prospects and global networks.",
  nav: [
    { label: "Home", href: "/" },
    { label: "About", href: "/about/" },
    { label: "Services", href: "/services/" },
    { label: "Gallery", href: "/gallery/" },
    { label: "Application Requirements", href: "/application-requirements/" },
    { label: "Contact Us", href: "/contact-us/" },
  ],
  email: "enquiries@nichotan.com",
  // Where the contact form posts its message: the Nicho Tan app's public website-message endpoint (see README).
  // Set NEXT_PUBLIC_ENQUIRY_URL at build time to point somewhere else, e.g. a local API.
  enquiryEndpoint: process.env.NEXT_PUBLIC_ENQUIRY_URL ?? "https://app.nichotan.com/api/enquiries/website",
  facebook: "https://www.facebook.com/profile.php?id=100067150133856",
  instagram: "https://www.instagram.com/nichotanconsulting/",
  linkedin: "https://www.linkedin.com/company/nichotan-consulting",
  // The student application lives in the Nicho Tan app, not on the website.
  applicationFormUrl: "https://app.nichotan.com/register",
  googleTag: "GT-NFR3C2DC",
  chatWidgetId: "6a99a003aacebf770319d4aa",
  // ICEF accreditation badge: the image and page it links to (what ICEF's own badge script inserts for account 7112).
  icefAgencyUrl: "https://www.icef.com/agency/0016M00002bSAl0QAG",
  icefBadgeImage: "https://icef-api-production.s3.eu-central-1.amazonaws.com/ias_material/0016M00002bSAl0QAG_badge.png",
  mapEmbed:
    "https://www.google.com/maps?q=Old+Hararians+Sports+Club,+Harare&output=embed",
  footer: "©2024 - Nicho Tan Consulting | All Rights Reserved.",
} as const;

/** Old WordPress links like "/about" become "/about/" so they match the static export's trailing-slash addresses. */
export function internal(href: string) {
  return href.endsWith("/") ? href : `${href}/`;
}
