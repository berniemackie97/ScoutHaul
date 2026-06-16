// Representative catalog used until the storefront is wired to the live API
// (and as a graceful fallback). Honest, specific copy — the brand voice.

export type Specimen = {
  id: string;
  slug: string;
  sku?: string;
  title: string;
  brand?: string;
  category: string;
  condition: string;
  price: number;
  compareAt?: number;
  era?: string;
  blurb: string;
};

export const sampleSpecimens: Specimen[] = [
  {
    id: "1", slug: "herman-miller-aeron-size-b", sku: "OVH-0042",
    title: "Herman Miller Aeron, Size B", brand: "Herman Miller", category: "Furniture",
    condition: "Excellent", price: 489, compareAt: 1395, era: "2018",
    blurb: "Fully functional tilt and lift. Light wear on armrests, noted in photos.",
  },
  {
    id: "2", slug: "sony-walkman-wm-dd9", sku: "OVH-0038",
    title: "Sony Walkman WM-DD9", brand: "Sony", category: "Electronics",
    condition: "Good — serviced", price: 340, era: "1989",
    blurb: "New belt fitted, speed verified. Cosmetic scuffs on the shell.",
  },
  {
    id: "3", slug: "vintage-snap-on-roll-cab", sku: "OVH-0051",
    title: "Snap-on 40\" Roll Cab", brand: "Snap-on", category: "Tools & Equipment",
    condition: "Good", price: 720, compareAt: 1850,
    blurb: "All drawers glide. One dented corner. Keys included.",
  },
  {
    id: "4", slug: "eames-style-lounge-walnut", sku: "OVH-0047",
    title: "Lounge Chair & Ottoman, Walnut", category: "Furniture",
    condition: "Very good", price: 615, compareAt: 1200, era: "1970s",
    blurb: "Leather supple, no cracks. Veneer intact. A genuine second life.",
  },
  {
    id: "5", slug: "canon-ae-1-program-50mm", sku: "OVH-0033",
    title: "Canon AE-1 Program + 50mm", brand: "Canon", category: "Electronics",
    condition: "Excellent — tested", price: 245, era: "1981",
    blurb: "Meter accurate, shutter clean across speeds. Fresh seals.",
  },
  {
    id: "6", slug: "pyrex-primary-bowl-set", sku: "OVH-0029",
    title: "Pyrex Primary Mixing Bowls, Set of 4", brand: "Pyrex", category: "Household",
    condition: "Good", price: 88, compareAt: 160, era: "1960s",
    blurb: "Bright colors, no chips. Minor utensil marks inside the largest.",
  },
  {
    id: "7", slug: "wilson-a2000-glove", sku: "OVH-0055",
    title: "Wilson A2000 Infield Glove", brand: "Wilson", category: "Sporting Goods",
    condition: "Very good", price: 110, compareAt: 270,
    blurb: "Broken in, fully laced, pocket holds. Ready to play.",
  },
  {
    id: "8", slug: "marantz-2230-receiver", sku: "OVH-0061",
    title: "Marantz 2230 Stereo Receiver", brand: "Marantz", category: "Electronics",
    condition: "Good — serviced", price: 530, compareAt: 950, era: "1972",
    blurb: "Recapped, lamps replaced, both channels strong. Wood case included.",
  },
];

export const sampleCategories = [
  { name: "Furniture", slug: "furniture", count: 45 },
  { name: "Electronics", slug: "electronics", count: 32 },
  { name: "Collectibles", slug: "collectibles", count: 28 },
  { name: "Household", slug: "household", count: 56 },
  { name: "Tools & Equipment", slug: "tools-equipment", count: 23 },
  { name: "Sporting Goods", slug: "sporting-goods", count: 19 },
];
