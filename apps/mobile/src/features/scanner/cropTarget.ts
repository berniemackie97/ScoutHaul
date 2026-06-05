// Pure geometry for focusing product identification on the framed target.
//
// The viewfinder reticle sits in the CENTER of the frame, so we crop the captured photo to a
// centered region (dropping peripheral clutter) and then downscale *that crop* — giving the model
// more effective resolution on the actual item instead of spending pixels on background.
//
// Deliberately dependency-free and deterministic so it can be regression-tested exhaustively:
// the failure mode of cropping is clipping the item, so every output is guaranteed in-bounds.

export type CropRect = { originX: number; originY: number; width: number; height: number };

// Crop covers this fraction of EACH dimension, centered. 0.8 → trims 10% off each edge: enough to
// shed edge clutter, conservative enough that a roughly-centered item is never clipped.
export const DEFAULT_CROP_FRACTION = 0.8;

// Never crop more than half the frame away — a hard floor so a bad fraction can't gut the image.
const MIN_FRACTION = 0.5;

/**
 * A centered crop covering `fraction` of each dimension of a `width`×`height` image (pixels).
 *
 * Guarantees (all regression-tested):
 *  - `originX >= 0` and `originY >= 0`
 *  - `originX + width  <= width`  and `originY + height <= height` (never out of bounds)
 *  - `1 <= width <= imageWidth` and `1 <= height <= imageHeight`
 *  - `fraction` is clamped to [0.5, 1]; `fraction >= 1` returns the full frame (no-op crop)
 *  - non-finite / non-positive inputs degrade safely to an in-bounds rect (never throws/NaN)
 */
export function computeCenterCrop(
  width: number,
  height: number,
  fraction: number = DEFAULT_CROP_FRACTION,
): CropRect {
  const iw = Math.max(1, Math.floor(Number.isFinite(width) ? width : 1));
  const ih = Math.max(1, Math.floor(Number.isFinite(height) ? height : 1));
  const f = Math.min(1, Math.max(MIN_FRACTION, Number.isFinite(fraction) ? fraction : DEFAULT_CROP_FRACTION));

  const cw = Math.min(iw, Math.max(1, Math.round(iw * f)));
  const ch = Math.min(ih, Math.max(1, Math.round(ih * f)));
  const originX = Math.max(0, Math.min(iw - cw, Math.round((iw - cw) / 2)));
  const originY = Math.max(0, Math.min(ih - ch, Math.round((ih - ch) / 2)));

  return { originX, originY, width: cw, height: ch };
}

// Cropping is only worth a manipulator pass when it actually removes something.
export function shouldCrop(rect: CropRect, width: number, height: number): boolean {
  return rect.width < Math.floor(width) || rect.height < Math.floor(height);
}
