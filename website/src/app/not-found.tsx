import Link from "next/link";
import Section from "@/components/Section";

export default function NotFound() {
  return (
    <Section className="pt-20">
      <div className="py-12 text-center">
        <h1 className="text-4xl font-bold text-ink">Page not found</h1>
        <p className="mt-4 text-muted">The page you are looking for does not exist or has moved.</p>
        <Link href="/" className="mt-8 inline-block rounded-md bg-forest px-6 py-3 font-semibold text-on-forest">Back to the home page</Link>
      </div>
    </Section>
  );
}
