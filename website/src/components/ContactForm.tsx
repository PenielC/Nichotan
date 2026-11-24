"use client";

import { useRef, useState } from "react";
import { site } from "@/content/site";

type State =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "sent" }
  | { kind: "error"; message: string }
  // The message could not be sent from here (offline, blocked); offer an email instead so nothing is lost.
  | { kind: "fallback"; mailto: string };

const PHONE_HINT = "Start the phone number with your country code, for example +263 77 123 4567.";

/** Sends the message to the Nicho Tan app, where it lands in the team's enquiries list. */
export default function ContactForm({ fields, button = "Submit" }: { fields: string[]; button?: string }) {
  const [email, name, phone, message] = fields;
  const [state, setState] = useState<State>({ kind: "idle" });
  const form = useRef<HTMLFormElement>(null);

  async function submit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const value = (k: string) => String(f.get(k) ?? "").trim();
    const phoneValue = value("phone");

    // The app keeps phone numbers in international form so the team can reach people on WhatsApp from anywhere.
    if (phoneValue && !/^(\+|00)/.test(phoneValue)) {
      setState({ kind: "error", message: PHONE_HINT });
      return;
    }

    setState({ kind: "sending" });
    try {
      const res = await fetch(site.enquiryEndpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: value("name"),
          email: value("email") || null,
          phone: phoneValue || null,
          message: value("message"),
          website: value("website"), // hidden trap field: real people leave it empty
        }),
      });
      if (res.ok) {
        form.current?.reset();
        setState({ kind: "sent" });
      } else if (res.status === 429) {
        setState({ kind: "error", message: "You have sent several messages in a short time. Please try again in a little while." });
      } else if (res.status === 400) {
        const problem = (await res.json().catch(() => null)) as { detail?: string } | null;
        setState({ kind: "error", message: problem?.detail || "Please check your details and try again." });
      } else {
        throw new Error(`status ${res.status}`);
      }
    } catch {
      const body = `${name}: ${value("name")}\n${email}: ${value("email")}\n${phone}: ${phoneValue}\n\n${value("message")}`;
      setState({ kind: "fallback", mailto: `mailto:${site.email}?subject=${encodeURIComponent(`Website enquiry from ${value("name")}`)}&body=${encodeURIComponent(body)}` });
    }
  }

  const input = "mt-1 w-full rounded-md border border-line bg-surface px-3 py-2 text-ink";
  const sending = state.kind === "sending";

  if (state.kind === "sent") {
    return (
      <div role="status" className="rounded-2xl border border-line bg-wash p-8">
        <p className="text-xl font-semibold text-ink">Thank you. Your message has been sent.</p>
        <p className="mt-2 text-muted">We will get back to you soon.</p>
        <button type="button" onClick={() => setState({ kind: "idle" })} className="mt-6 rounded-md border border-forest px-5 py-2 text-sm font-semibold text-forest hover:bg-white">
          Send another message
        </button>
      </div>
    );
  }

  return (
    <form ref={form} onSubmit={submit} className="space-y-4">
      <label className="block text-sm font-medium">{name}
        <input name="name" required maxLength={150} autoComplete="name" className={input} />
      </label>
      <label className="block text-sm font-medium">{email}
        <input name="email" type="email" required maxLength={256} autoComplete="email" className={input} />
      </label>
      <label className="block text-sm font-medium">{phone}
        <input name="phone" type="tel" maxLength={30} autoComplete="tel" placeholder="+263 77 123 4567" aria-describedby="phone-hint" className={input} />
        <span id="phone-hint" className="mt-1 block text-xs font-normal text-muted">{PHONE_HINT}</span>
      </label>
      <label className="block text-sm font-medium">{message}
        <textarea name="message" required maxLength={2000} rows={5} className={input} />
      </label>
      {/* Trap field for simple bots: hidden from people and from assistive technology. */}
      <div aria-hidden className="absolute -left-[9999px] h-0 w-0 overflow-hidden">
        <label>Website<input name="website" tabIndex={-1} autoComplete="off" /></label>
      </div>
      <button type="submit" disabled={sending} className="rounded-md bg-forest px-6 py-3 font-semibold text-on-forest hover:brightness-110 disabled:opacity-60">
        {sending ? "Sending…" : button}
      </button>
      {state.kind === "error" && <p role="alert" className="text-sm font-medium text-red-700">{state.message}</p>}
      {state.kind === "fallback" && (
        <p role="alert" className="text-sm text-ink">
          We could not send your message from here. <a className="font-semibold underline" href={state.mailto}>Email it to us instead</a>, or write to {site.email}.
        </p>
      )}
    </form>
  );
}
