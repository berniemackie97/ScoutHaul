// expo-router cannot pass data back through router.back(), so a scan opened from the item editor
// leaves its result here and the editor takes it on focus.

export type ScanHandoff = {
  title: string;
  brand: string | null;
  model: string | null;
  category: string | null;
  color: string | null;
  condition: string | null;
  barcode: string | null;
  estimatedSalePrice: number | null;
  photoUri: string | null;
  /** Replace what the editor holds rather than only filling its blanks. */
  overwrite: boolean;
};

let pending: ScanHandoff | null = null;

export function handOffScan(result: ScanHandoff): void {
  pending = result;
}

export function takeScanHandoff(): ScanHandoff | null {
  const result = pending;
  pending = null;
  return result;
}
