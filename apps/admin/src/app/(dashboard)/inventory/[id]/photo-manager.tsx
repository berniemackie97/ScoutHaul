"use client";

import { useRef, useState, useTransition } from "react";
import { ImageIcon, Star, Trash2, Upload } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle } from "@overhaul/ui";
import {
  deleteInventoryPhoto,
  setInventoryHeroPhoto,
  uploadInventoryPhoto,
} from "./actions";

export type ManagedPhoto = {
  id: string;
  imageUrl: string;
  apiUrl: string;
  isHero: boolean;
};

export function PhotoManager({ id, photos }: { id: string; photos: ManagedPhoto[] }) {
  const formRef = useRef<HTMLFormElement>(null);
  const [message, setMessage] = useState("");
  const [isPending, startTransition] = useTransition();

  function upload(formData: FormData) {
    startTransition(async () => {
      const result = await uploadInventoryPhoto(id, formData);
      setMessage(result.message);
      if (result.ok) formRef.current?.reset();
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
        <CardDescription>Manage item photography and choose the image used as the primary storefront visual.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {photos.length === 0 ? (
          <div className="flex min-h-36 flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 text-center">
            <ImageIcon className="mb-2 h-6 w-6 text-muted-foreground" />
            <p className="text-sm font-medium">No photos uploaded</p>
            <p className="text-xs text-muted-foreground">The first photo becomes the hero automatically.</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-3">
            {photos.map((photo) => (
              <div key={photo.id} className="group relative overflow-hidden rounded-lg border bg-muted">
                {/* The API serves authenticated inventory images from stable, unguessable URLs. */}
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

        <form ref={formRef} action={upload} className="space-y-3 rounded-lg border bg-muted/20 p-3">
          <input
            name="file"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            required
            className="block w-full text-xs file:mr-3 file:rounded-md file:border-0 file:bg-secondary file:px-3 file:py-2 file:text-xs file:font-medium"
          />
          <label className="flex items-center gap-2 text-xs text-muted-foreground">
            <input name="isHero" type="checkbox" className="h-4 w-4 rounded border-input" />
            Make this the hero image
          </label>
          <Button type="submit" variant="outline" className="w-full" disabled={isPending}>
            <Upload className="h-4 w-4" /> {isPending ? "Working…" : "Upload photo"}
          </Button>
        </form>
        {message && <p className="text-sm text-muted-foreground" aria-live="polite">{message}</p>}
      </CardContent>
    </Card>
  );
}
