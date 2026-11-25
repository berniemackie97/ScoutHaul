/** What a tag scan is checked against: anything printed on or known about the item. */
export type ScanTarget = { id: string; barcode?: string | null; sku?: string | null };

const normalize = (value: string) => value.trim().toLowerCase().replace(/[\s-]/g, "");

/**
 * Whether a scanned or typed code names this item: its barcode or SKU exactly, or its id — whole, or the first eight
 * characters printed on a HaulScout tag. A wrong tag is caught here, before the wrong thing is packed.
 */
export function scanMatches(scan: string, target: ScanTarget): boolean {
  const code = normalize(scan);
  if (code.length < 4) return false;
  const id = normalize(target.id);
  return (
    [target.barcode, target.sku].some((value) => value && normalize(value) === code) ||
    code === id ||
    (code.length >= 8 && id.startsWith(code))
  );
}
