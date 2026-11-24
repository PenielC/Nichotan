import Image from "next/image";

export default function PageHero({ title }: { title: string }) {
  return (
    <section className="relative isolate flex min-h-56 items-center justify-center overflow-hidden bg-ink text-white">
      <Image src="/images/page-header.webp" alt="" fill sizes="100vw" className="-z-10 object-cover opacity-40" priority />
      <h1 className="px-4 pb-16 pt-32 text-center text-4xl font-bold tracking-tight md:text-5xl">{title}</h1>
    </section>
  );
}
