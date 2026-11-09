import { useEffect, useState } from "react";
import { Image, type ImageProps } from "expo-image";
import { authStore } from "../services/auth";
import { getActiveOrganizationId } from "../services/workspaceSession";
import { isPrivateInventoryPhotoUrl, resolvePhotoUrl } from "../utils/photoUrl";

type PhotoImageProps = Omit<ImageProps, "source"> & { uri?: string | null };

export function PhotoImage({ uri, cachePolicy, ...props }: PhotoImageProps) {
  const [scope, setScope] = useState<{ token: string | null; organizationId: string | null } | null>(null);
  const privatePhoto = Boolean(uri && isPrivateInventoryPhotoUrl(uri));

  useEffect(() => {
    if (!privatePhoto) return;
    let live = true;
    const read = async () => {
      const organizationId = await getActiveOrganizationId();
      if (live) setScope({ organizationId, token: authStore.getToken() });
    };
    void read();
    const unsubscribe = authStore.subscribe(() => void read());
    return () => { live = false; unsubscribe(); };
  }, [privatePhoto, uri]);

  const resolved = resolvePhotoUrl(uri);
  if (!resolved) return null;
  if (privatePhoto && (!scope?.token || !scope.organizationId)) return null;
  const source = privatePhoto
    ? { uri: resolved, headers: { Authorization: `Bearer ${scope!.token}`, "X-HaulScout-Organization-Id": scope!.organizationId! } }
    : { uri: resolved };

  return <Image {...props} source={source} cachePolicy={privatePhoto ? "none" : (cachePolicy ?? "disk")} />;
}
