"use client";

import { useRef, useState, useTransition } from "react";
import { ImageIcon, Link as LinkIcon, Star, Trash2, Upload } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import {
  deleteInventoryPhoto,
  importInventoryPhotosFromUrls,
  setInventoryHeroPhoto,
  uploadInventoryPhoto,
} from "./actions";

export type ManagedPhoto = {
  id: string;
  imageUrl: string;
  apiUrl: string;
  isHero: boolean;
};

const ACCEPTED = ["image/jpeg", "image/png", "image/webp"];

export function PhotoManager({ id, photos }: { id: string; photos: ManagedPhoto[] }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [message, setMessage] = useState("");
  const [dragging, setDragging] = useState(false);
  const [progress, setProgress] = useState<{ done: number; total: number } | null>(null);
  const [isPending, startTransition] = useTransition();
  const [urlOpen, setUrlOpen] = useState(false);
  const [urls, setUrls] = useState("");
  const [firstIsHero, setFirstIsHero] = useState(true);

  function importUrls() {
    startTransition(async () => {
      const result = await importInventoryPhotosFromUrls(id, urls, firstIsHero && photos.length === 0);
      setMessage(result.message);
      if (result.ok) {
        setUrls("");
        setUrlOpen(false);
      }
    });
  }

  function uploadFiles(fileList: FileList | File[]) {
    const files = Array.from(fileList).filter((file) => ACCEPTED.includes(file.type) && file.size > 0 && file.size <= 10_000_000);
    if (files.length === 0) {
      setMessage("Choose JPEG, PNG, or WebP images under 10 MB.");
      return;
    }
    startTransition(async () => {
      let done = 0;
      let failed = 0;
      setProgress({ done: 0, total: files.length });
      // Upload sequentially so the first image reliably becomes the hero and the server isn't hammered.
      for (const file of files) {
        const formData = new FormData();
        formData.set("file", file);
        const result = await uploadInventoryPhoto(id, formData);
        if (!result.ok) failed += 1;
        done += 1;
        setProgress({ done, total: files.length });
      }
      setProgress(null);
      setMessage(failed === 0 ? `Uploaded ${files.length} photo${files.length === 1 ? "" : "s"}.` : `Uploaded ${files.length - failed} of ${files.length}; ${failed} failed.`);
    });
  }

  function setHero(photo: ManagedPhoto) {
    startTransition(async () => {
      const result = await setInventoryHeroPhoto(id, photo.apiUrl);
      setMessage(result.message);
    });
  }

  function remove(photo: ManagedPhoto) {
    if (!window.confirm("Delete this photo permanently?")) return;
    startTransition(async () => {
      const result = await deleteInventoryPhoto(id, photo.id);
      setMessage(result.message);
    });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-lg">Photos</CardTitle>
        <CardDescription>Drag & drop or select multiple images. The first becomes the hero; reorder by setting a new hero.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {photos.length > 0 && (
          <div className="grid grid-cols-2 gap-3">
            {photos.map((photo) => (
              <div key={photo.id} className="group relative overflow-hidden rounded-lg border bg-muted">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={photo.imageUrl} alt="Inventory item" className="aspect-square h-full w-full object-cover" />
                {photo.isHero && (
                  <span className="absolute left-2 top-2 inline-flex items-center gap-1 rounded-full bg-black/75 px-2 py-1 text-[11px] font-medium text-white">
                    <Star className="h-3 w-3 fill-current" /> Hero
                  </span>
                )}
                <div className="absolute inset-x-0 bottom-0 flex justify-end gap-1 bg-gradient-to-t from-black/80 to-transparent p-2 pt-8 opacity-100 sm:opacity-0 sm:transition-opacity sm:group-hover:opacity-100">
                  {!photo.isHero && (
                    <Button type="button" size="icon" variant="secondary" className="h-8 w-8" title="Set as hero" disabled={isPending} onClick={() => setHero(photo)}>
                      <Star className="h-4 w-4" />
                    </Button>
                  )}
                  <Button type="button" size="icon" variant="destructive" className="h-8 w-8" title="Delete photo" disabled={isPending} onClick={() => remove(photo)}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            ))}
          </div>
        )}

        <div
          onDragOver={(event) => { event.preventDefault(); setDragging(true); }}
          onDragLeave={() => setDragging(false)}
          onDrop={(event) => { event.preventDefault(); setDragging(false); uploadFiles(event.dataTransfer.files); }}
          onClick={() => inputRef.current?.click()}
          role="button"
          tabIndex={0}
          onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") inputRef.current?.click(); }}
          className={`flex min-h-32 cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed p-6 text-center transition ${
            dragging ? "border-brand-500 bg-brand-50/50" : "bg-muted/20 hover:bg-muted/40"
          }`}
        >
          <Upload className="text-muted-foreground mb-2 h-6 w-6" />
          <p className="text-sm font-medium">{isPending && progress ? `Uploading ${progress.done}/${progress.total}…` : "Drag & drop photos, or click to browse"}</p>
          <p className="text-muted-foreground text-xs">JPEG, PNG, or WebP · up to 10 MB each · multiple at once</p>
          <input
            ref={inputRef}
            type="file"
            accept={ACCEPTED.join(",")}
            multiple
            className="hidden"
            onChange={(event) => { if (event.target.files) uploadFiles(event.target.files); event.target.value = ""; }}
          />
        </div>

        {/* Import by URL — for photos that live on a listing you already published (eBay, Vinted,
            a supplier page) instead of on this machine. Beats screenshotting each image. */}
        <div className="rounded-lg border bg-muted/10 p-3">
          {urlOpen ? (
            <div className="space-y-2">
              <label htmlFor="photo-urls" className="text-sm font-medium">Paste image addresses</label>
              <p className="text-muted-foreground text-xs">
                Right-click an image on the listing → <em>Copy image address</em>. One per line, up to 12.
                A page link won&rsquo;t work — it has to be the image itself.
              </p>
              <textarea
                id="photo-urls"
                rows={3}
                value={urls}
                onChange={(event) => setUrls(event.target.value)}
                placeholder={"https://i.ebayimg.com/images/g/…/s-l1600.jpg\nhttps://images1.vinted.net/…jpeg"}
                className="w-full rounded-lg border border-input bg-background p-2 font-mono text-xs"
                disabled={isPending}
              />
              <div className="flex flex-wrap items-center gap-3">
                <Button type="button" size="sm" disabled={isPending || !urls.trim()} onClick={importUrls}>
                  {isPending ? "Importing…" : "Import photos"}
                </Button>
                <Button type="button" size="sm" variant="ghost" disabled={isPending} onClick={() => { setUrlOpen(false); setUrls(""); }}>
                  Cancel
                </Button>
                {photos.length === 0 && (
                  <label className="text-muted-foreground flex items-center gap-1.5 text-xs">
                    <input type="checkbox" checked={firstIsHero} onChange={(event) => setFirstIsHero(event.target.checked)} />
                    Make the first one the hero
                  </label>
                )}
              </div>
            </div>
          ) : (
            <button type="button" onClick={() => setUrlOpen(true)} className="text-brand-600 flex items-center gap-2 text-sm hover:underline">
              <LinkIcon className="h-4 w-4" /> Import from a URL instead
            </button>
          )}
        </div>

        {photos.length === 0 && !isPending && (
          <div className="text-muted-foreground flex items-center gap-2 text-xs">
            <ImageIcon className="h-4 w-4" /> No photos yet — the first one you add becomes the hero.
          </div>
        )}
        {message && <p className="text-muted-foreground text-sm" aria-live="polite">{message}</p>}
      </CardContent>
    </Card>
  );
}
