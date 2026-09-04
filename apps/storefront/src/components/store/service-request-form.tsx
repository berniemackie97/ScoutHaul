"use client";

import { useRef, useState } from "react";
import { BadgeCheck, Camera, Check, ChevronLeft, ChevronRight, Loader2, X } from "lucide-react";

type Kind = "consignment" | "repair";
type FieldType = "text" | "textarea" | "select" | "number";
type FieldDef = {
  name: string;
  label: string;
  type: FieldType;
  required?: boolean;
  placeholder?: string;
  options?: string[];
  half?: boolean;
};

const CONFIG: Record<
  Kind,
  { itemTitle: string; itemBlurb: string; photoBlurb: string; submitLabel: string; itemFields: FieldDef[] }
> = {
  consignment: {
    itemTitle: "What are you selling?",
    itemBlurb: "The more you tell us, the faster we can say yes and give you a number.",
    photoBlurb: "Clear photos let us price it right. Show it from a few angles, plus any flaws, labels, or serial numbers.",
    submitLabel: "Submit for review",
    itemFields: [
      { name: "item", label: "What is it?", type: "text", required: true, placeholder: "e.g. Eames-style lounge chair & ottoman" },
      { name: "category", label: "Category", type: "select", required: true, half: true, options: ["Furniture", "Electronics", "Collectibles", "Clothing", "Household", "Other"] },
      { name: "condition", label: "Condition", type: "select", required: true, half: true, options: ["Excellent", "Very good", "Good", "Fair", "Project / parts"] },
      { name: "askingPrice", label: "What would you like to get? ($)", type: "number", placeholder: "Optional — leave blank if you're not sure" },
      { name: "details", label: "Anything we should know?", type: "textarea", placeholder: "Age, history, flaws, accessories, original receipts…" },
    ],
  },
  repair: {
    itemTitle: "What needs fixing?",
    itemBlurb: "Tell us what it is and what's wrong. We'll come back with whether we can help and a rough quote.",
    photoBlurb: "Show us the item and the problem area — plus any label with the model number.",
    submitLabel: "Request a quote",
    itemFields: [
      { name: "item", label: "What is it?", type: "text", required: true, placeholder: "e.g. Marantz 2270 stereo receiver" },
      { name: "category", label: "Type", type: "select", required: true, half: true, options: ["Electronics", "Furniture", "Appliance", "Instrument", "Other"] },
      { name: "brandModel", label: "Brand & model", type: "text", half: true, placeholder: "If you know it" },
      { name: "problem", label: "What's wrong?", type: "textarea", required: true, placeholder: "What it does, when it started, anything you've already tried…" },
    ],
  },
};

const MAX_PHOTOS = 6;
const inputClass =
  "w-full border border-rule bg-background px-4 py-3 text-base text-ink outline-none transition-colors placeholder:text-slate/50 focus:border-copper";
const labelClass = "mb-2 block font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70";

type Photo = { id: string; name: string; dataUrl: string };
const steps = ["Item", "Photos", "You"] as const;

export function ServiceRequestForm({ kind }: { kind: Kind }) {
  const config = CONFIG[kind];
  const [step, setStep] = useState(0);
  const [item, setItem] = useState<Record<string, string>>({});
  const [photos, setPhotos] = useState<Photo[]>([]);
  const [contact, setContact] = useState({ name: "", email: "", phone: "" });
  const [company, setCompany] = useState(""); // honeypot
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  function setField(name: string, value: string) {
    setItem((current) => ({ ...current, [name]: value }));
  }

  async function onFiles(list: FileList | null) {
    if (!list) return;
    setError(null);
    const room = MAX_PHOTOS - photos.length;
    const incoming = Array.from(list).filter((file) => file.type.startsWith("image/")).slice(0, room);
    const added: Photo[] = [];
    for (const file of incoming) {
      try {
        const dataUrl = await compressImage(file);
        added.push({ id: `${Date.now()}-${Math.random().toString(36).slice(2)}`, name: file.name, dataUrl });
      } catch {
        setError("One of those images could not be read. Try a JPG or PNG.");
      }
    }
    if (added.length) setPhotos((current) => [...current, ...added].slice(0, MAX_PHOTOS));
    if (fileRef.current) fileRef.current.value = "";
  }

  function removePhoto(id: string) {
    setPhotos((current) => current.filter((photo) => photo.id !== id));
  }

  function itemComplete() {
    return config.itemFields.every((field) => !field.required || (item[field.name] ?? "").trim().length > 0);
  }
  const contactComplete = contact.name.trim().length > 0 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(contact.email.trim());

  function next() {
    setError(null);
    if (step === 0 && !itemComplete()) return setError("Please fill in the required fields.");
    if (step === 1 && photos.length === 0) return setError("Add at least one photo so we can take a look.");
    setStep((s) => Math.min(s + 1, steps.length - 1));
  }

  async function submit() {
    if (!contactComplete) return setError("Please add your name and a valid email.");
    setSubmitting(true);
    setError(null);
    try {
      const response = await fetch("/api/service-requests", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          kind,
          item,
          contact,
          company,
          photos: photos.map((photo) => ({ name: photo.name, dataUrl: photo.dataUrl })),
        }),
      });
      if (!response.ok) {
        const payload = (await response.json().catch(() => ({}))) as { error?: string };
        throw new Error(payload.error ?? "Something went wrong. Please try again.");
      }
      setDone(true);
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : "Something went wrong. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  if (done) {
    return (
      <div className="mx-auto max-w-md py-10 text-center">
        <span className="inline-flex h-16 w-16 items-center justify-center rounded-full border-2 border-copper text-copper">
          <BadgeCheck className="h-8 w-8" aria-hidden />
        </span>
        <h2 className="mt-7 font-heading text-3xl font-semibold tracking-[-0.02em] text-ink">Request received.</h2>
        <p className="mt-3 leading-relaxed text-slate">
          Thanks — we&rsquo;ve got your {kind === "repair" ? "repair request" : "item"} and your photos. Expect a reply
          at <span className="text-ink">{contact.email}</span> within 48 hours.
        </p>
      </div>
    );
  }

  return (
    <div>
      {/* Stepper */}
      <ol className="mb-8 flex items-center gap-2">
        {steps.map((label, index) => (
          <li key={label} className="flex flex-1 items-center gap-2">
            <span
              className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full font-mono text-xs transition-colors ${
                index < step ? "bg-copper text-background" : index === step ? "bg-ink text-background" : "border border-rule text-slate/60"
              }`}
            >
              {index < step ? <Check className="h-3.5 w-3.5" aria-hidden /> : index + 1}
            </span>
            <span className={`font-mono text-[11px] uppercase tracking-[0.14em] ${index === step ? "text-ink" : "text-slate/60"}`}>
              {label}
            </span>
            {index < steps.length - 1 ? <span className="ml-1 hidden h-px flex-1 bg-rule sm:block" /> : null}
          </li>
        ))}
      </ol>

      {step === 0 ? (
        <div className="space-y-5">
          <div>
            <h3 className="font-heading text-2xl font-semibold text-ink">{config.itemTitle}</h3>
            <p className="mt-2 text-sm leading-relaxed text-slate">{config.itemBlurb}</p>
          </div>
          <div className="grid gap-5 sm:grid-cols-2">
            {config.itemFields.map((field) => (
              <div key={field.name} className={field.half ? "" : "sm:col-span-2"}>
                <label htmlFor={field.name} className={labelClass}>
                  {field.label}
                  {field.required ? <span className="text-copper"> *</span> : null}
                </label>
                {field.type === "textarea" ? (
                  <textarea
                    id={field.name}
                    rows={3}
                    value={item[field.name] ?? ""}
                    onChange={(event) => setField(field.name, event.target.value)}
                    placeholder={field.placeholder}
                    className={inputClass}
                  />
                ) : field.type === "select" ? (
                  <select
                    id={field.name}
                    value={item[field.name] ?? ""}
                    onChange={(event) => setField(field.name, event.target.value)}
                    className={inputClass}
                  >
                    <option value="" disabled>
                      Select…
                    </option>
                    {field.options?.map((option) => (
                      <option key={option}>{option}</option>
                    ))}
                  </select>
                ) : (
                  <input
                    id={field.name}
                    type={field.type === "number" ? "number" : "text"}
                    min={field.type === "number" ? 0 : undefined}
                    value={item[field.name] ?? ""}
                    onChange={(event) => setField(field.name, event.target.value)}
                    placeholder={field.placeholder}
                    className={inputClass}
                  />
                )}
              </div>
            ))}
          </div>
        </div>
      ) : null}

      {step === 1 ? (
        <div className="space-y-5">
          <div>
            <h3 className="font-heading text-2xl font-semibold text-ink">Add photos</h3>
            <p className="mt-2 text-sm leading-relaxed text-slate">{config.photoBlurb}</p>
          </div>
          <div className="grid grid-cols-3 gap-3 sm:grid-cols-4">
            {photos.map((photo) => (
              <div key={photo.id} className="group relative aspect-square overflow-hidden border border-rule bg-surface">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={photo.dataUrl} alt="" className="h-full w-full object-cover" />
                <button
                  type="button"
                  onClick={() => removePhoto(photo.id)}
                  aria-label="Remove photo"
                  className="absolute right-1 top-1 flex h-6 w-6 items-center justify-center rounded-full bg-ink/80 text-background transition-colors hover:bg-copper hover:text-background"
                >
                  <X className="h-3.5 w-3.5" aria-hidden />
                </button>
              </div>
            ))}
            {photos.length < MAX_PHOTOS ? (
              <button
                type="button"
                onClick={() => fileRef.current?.click()}
                className="flex aspect-square flex-col items-center justify-center gap-2 border border-dashed border-rule bg-surface text-slate transition-colors hover:border-copper hover:text-copper"
              >
                <Camera className="h-6 w-6" aria-hidden />
                <span className="font-mono text-[10px] uppercase tracking-[0.14em]">Add</span>
              </button>
            ) : null}
          </div>
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            multiple
            onChange={(event) => onFiles(event.target.files)}
            className="hidden"
          />
          <p className="font-mono text-[11px] uppercase tracking-[0.14em] text-slate/60">
            {photos.length} / {MAX_PHOTOS} added · JPG or PNG
          </p>
        </div>
      ) : null}

      {step === 2 ? (
        <div className="space-y-5">
          <div>
            <h3 className="font-heading text-2xl font-semibold text-ink">Where do we reach you?</h3>
            <p className="mt-2 text-sm leading-relaxed text-slate">We&rsquo;ll only use this to reply about your request.</p>
          </div>
          <div className="grid gap-5 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label htmlFor="name" className={labelClass}>Your name <span className="text-copper">*</span></label>
              <input id="name" value={contact.name} onChange={(e) => setContact((c) => ({ ...c, name: e.target.value }))} placeholder="Full name" className={inputClass} />
            </div>
            <div>
              <label htmlFor="email" className={labelClass}>Email <span className="text-copper">*</span></label>
              <input id="email" type="email" value={contact.email} onChange={(e) => setContact((c) => ({ ...c, email: e.target.value }))} placeholder="you@email.com" className={inputClass} />
            </div>
            <div>
              <label htmlFor="phone" className={labelClass}>Phone (optional)</label>
              <input id="phone" type="tel" value={contact.phone} onChange={(e) => setContact((c) => ({ ...c, phone: e.target.value }))} placeholder="(941) 555-0123" className={inputClass} />
            </div>
          </div>
          <div className="border border-rule bg-surface/60 p-4">
            <p className="font-mono text-[10px] uppercase tracking-[0.18em] text-copper">Review</p>
            <dl className="mt-3 space-y-1.5 text-sm">
              {config.itemFields
                .filter((field) => (item[field.name] ?? "").trim())
                .map((field) => (
                  <div key={field.name} className="flex gap-3">
                    <dt className="w-32 shrink-0 text-slate/60">{field.label.replace(/\s*\(.*\)$/, "")}</dt>
                    <dd className="text-ink">{item[field.name]}</dd>
                  </div>
                ))}
              <div className="flex gap-3">
                <dt className="w-32 shrink-0 text-slate/60">Photos</dt>
                <dd className="text-ink">{photos.length} attached</dd>
              </div>
            </dl>
          </div>
        </div>
      ) : null}

      {/* Honeypot — hidden from users, catches bots. */}
      <div aria-hidden className="absolute h-0 w-0 overflow-hidden">
        <label>
          Company
          <input tabIndex={-1} autoComplete="off" value={company} onChange={(e) => setCompany(e.target.value)} />
        </label>
      </div>

      {error ? (
        <p className="mt-5 border border-red-500/25 bg-red-500/10 px-4 py-3 text-sm text-red-700">{error}</p>
      ) : null}

      {/* Controls */}
      <div className="mt-8 flex items-center justify-between gap-4">
        <button
          type="button"
          onClick={() => {
            setError(null);
            setStep((s) => Math.max(s - 1, 0));
          }}
          disabled={step === 0 || submitting}
          className="inline-flex items-center gap-1.5 font-mono text-[11px] uppercase tracking-[0.16em] text-slate transition-colors hover:text-ink disabled:invisible"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden /> Back
        </button>

        {step < steps.length - 1 ? (
          <button
            type="button"
            onClick={next}
            className="group inline-flex items-center gap-2 bg-ink px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper hover:text-background"
          >
            Continue
            <ChevronRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" aria-hidden />
          </button>
        ) : (
          <button
            type="button"
            onClick={submit}
            disabled={submitting}
            className="inline-flex items-center gap-2 bg-copper px-6 py-3.5 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-transform hover:-translate-y-0.5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {submitting ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
            {submitting ? "Sending…" : config.submitLabel}
          </button>
        )}
      </div>
    </div>
  );
}

/** Downscale + re-encode to JPEG so uploads stay small enough to email as attachments and fit serverless body limits. */
async function compressImage(file: File, maxDim = 1400, quality = 0.75): Promise<string> {
  const image = await loadImage(file);
  const scale = Math.min(1, maxDim / Math.max(image.width, image.height));
  const width = Math.max(1, Math.round(image.width * scale));
  const height = Math.max(1, Math.round(image.height * scale));
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (!context) throw new Error("no canvas context");
  context.drawImage(image, 0, 0, width, height);
  return canvas.toDataURL("image/jpeg", quality);
}

function loadImage(file: File): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const image = new Image();
    image.onload = () => {
      URL.revokeObjectURL(url);
      resolve(image);
    };
    image.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error("image decode failed"));
    };
    image.src = url;
  });
}
