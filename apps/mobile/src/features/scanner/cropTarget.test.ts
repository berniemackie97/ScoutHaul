import { computeCenterCrop, shouldCrop, DEFAULT_CROP_FRACTION } from './cropTarget';

// Property: a crop must never fall outside the source image, never be empty, never be NaN.
function expectInBounds(rect: { originX: number; originY: number; width: number; height: number }, w: number, h: number) {
  expect(Number.isInteger(rect.originX)).toBe(true);
  expect(Number.isInteger(rect.originY)).toBe(true);
  expect(Number.isInteger(rect.width)).toBe(true);
  expect(Number.isInteger(rect.height)).toBe(true);
  expect(rect.originX).toBeGreaterThanOrEqual(0);
  expect(rect.originY).toBeGreaterThanOrEqual(0);
  expect(rect.width).toBeGreaterThanOrEqual(1);
  expect(rect.height).toBeGreaterThanOrEqual(1);
  expect(rect.originX + rect.width).toBeLessThanOrEqual(Math.floor(w));
  expect(rect.originY + rect.height).toBeLessThanOrEqual(Math.floor(h));
}

describe('computeCenterCrop', () => {
  it('crops a centered 80% region of a landscape photo', () => {
    expect(computeCenterCrop(4000, 3000, 0.8)).toEqual({ originX: 400, originY: 300, width: 3200, height: 2400 });
  });

  it('crops a centered 80% region of a portrait photo', () => {
    expect(computeCenterCrop(1080, 1920, 0.8)).toEqual({ originX: 108, originY: 192, width: 864, height: 1536 });
  });

  it('crops a centered 80% region of a square photo', () => {
    expect(computeCenterCrop(100, 100, 0.8)).toEqual({ originX: 10, originY: 10, width: 80, height: 80 });
  });

  it('fraction >= 1 returns the full frame (no-op crop)', () => {
    expect(computeCenterCrop(4000, 3000, 1)).toEqual({ originX: 0, originY: 0, width: 4000, height: 3000 });
    expect(computeCenterCrop(4000, 3000, 1.5)).toEqual({ originX: 0, originY: 0, width: 4000, height: 3000 });
  });

  it('clamps an overly aggressive fraction to the 0.5 floor (never guts the item)', () => {
    expect(computeCenterCrop(1000, 1000, 0.1)).toEqual({ originX: 250, originY: 250, width: 500, height: 500 });
    expect(computeCenterCrop(1000, 1000, 0)).toEqual({ originX: 250, originY: 250, width: 500, height: 500 });
  });

  it('uses the default fraction when none is given', () => {
    expect(computeCenterCrop(1000, 1000)).toEqual(computeCenterCrop(1000, 1000, DEFAULT_CROP_FRACTION));
  });

  it('stays in-bounds across many sizes, fractions, and odd dimensions', () => {
    const dims = [1, 2, 3, 99, 100, 101, 999, 1000, 1001, 2048, 4000, 4032, 3024];
    const fracs = [0.5, 0.6, 0.7, 0.75, 0.8, 0.9, 0.95, 1];
    for (const w of dims) {
      for (const h of dims) {
        for (const f of fracs) {
          expectInBounds(computeCenterCrop(w, h, f), w, h);
        }
      }
    }
  });

  it('degrades safely on invalid input instead of throwing or returning NaN', () => {
    for (const [w, h] of [[0, 0], [-100, 200], [NaN, 100], [Infinity, 100], [100, NaN]] as [number, number][]) {
      const rect = computeCenterCrop(w, h, 0.8);
      expect(Number.isNaN(rect.originX)).toBe(false);
      expect(Number.isNaN(rect.width)).toBe(false);
      expect(rect.width).toBeGreaterThanOrEqual(1);
      expect(rect.height).toBeGreaterThanOrEqual(1);
      expect(rect.originX).toBeGreaterThanOrEqual(0);
      expect(rect.originY).toBeGreaterThanOrEqual(0);
    }
  });

  it('tiny 1x1 image yields a valid 1x1 crop', () => {
    expect(computeCenterCrop(1, 1, 0.8)).toEqual({ originX: 0, originY: 0, width: 1, height: 1 });
  });
});

describe('shouldCrop', () => {
  it('is true when the rect is smaller than the source', () => {
    expect(shouldCrop({ originX: 400, originY: 300, width: 3200, height: 2400 }, 4000, 3000)).toBe(true);
  });
  it('is false for a full-frame rect (nothing to remove)', () => {
    expect(shouldCrop({ originX: 0, originY: 0, width: 4000, height: 3000 }, 4000, 3000)).toBe(false);
  });
});
