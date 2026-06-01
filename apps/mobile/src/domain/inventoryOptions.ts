import type { InventoryStatus } from './types';

export const conditionOptions = [
  'New with tags',
  'New without tags',
  'Open box',
  'Near mint',
  'Light use',
  'Moderate use',
  'Heavy use',
  'Damaged',
  'For parts',
] as const;

export const categoryOptions = [
  'Clothing',
  'Shoes',
  'Tech',
  'Appliances',
  'Furniture',
  'Decor',
  'Tools',
  'Electronics',
  'Video Games',
  'Toys',
  'Collectibles',
  'Home',
  'Books',
  'Media',
  'Tools',
  'Other',
] as const;

export const sizeSystemOptions = [
  'US letter',
  'US numeric',
  'Waist/Inseam',
  'Shoe US M',
  'Shoe US W',
  'Youth',
  'One size',
  'Other',
] as const;

export const damageSeverityOptions = [
  'Cosmetic',
  'Minor',
  'Moderate',
  'Major',
  'Parts only',
] as const;

export const damageTypeOptions = [
  'Stain',
  'Hole/tear',
  'Scuff/scratch',
  'Missing part',
  'Odor',
  'Does not power on',
  'Untested',
  'Other',
] as const;

export const workflowActionOptions: { status: InventoryStatus; label: string }[] = [
  { status: 'NeedsCleaning', label: 'Needs cleaning' },
  { status: 'NeedsTesting', label: 'Needs testing' },
  { status: 'NeedsPhotos', label: 'Needs photos' },
  { status: 'ReadyToList', label: 'Ready to list' },
  { status: 'Listed', label: 'Listed' },
];

export type InventoryAttributeField = {
  key: string;
  label: string;
  placeholder: string;
  multiline?: boolean;
};

export const categoryAttributeFields: Record<string, InventoryAttributeField[]> = {
  Clothing: [
    { key: 'material', label: 'Material / fabric', placeholder: 'Cotton, wool, polyester blend' },
    { key: 'materialPercentages', label: 'Material percentages', placeholder: '80% cotton, 20% polyester' },
    { key: 'genderFit', label: 'Fit / department', placeholder: 'Men, women, youth, unisex, slim fit' },
    { key: 'measurements', label: 'Measurements', placeholder: 'Chest, length, sleeve, waist', multiline: true },
    { key: 'care', label: 'Care notes', placeholder: 'Machine wash, dry clean only' },
  ],
  Shoes: [
    { key: 'shoeSize', label: 'Shoe size', placeholder: 'US 10.5 M, EU 44' },
    { key: 'width', label: 'Width', placeholder: 'D, EE, narrow, wide' },
    { key: 'upperMaterial', label: 'Upper material', placeholder: 'Leather, suede, mesh' },
    { key: 'soleCondition', label: 'Sole condition', placeholder: 'Heel drag, tread %, separation' },
  ],
  Tech: [
    { key: 'serialNumber', label: 'Serial / IMEI', placeholder: 'Serial, IMEI, service tag' },
    { key: 'powerStatus', label: 'Power status', placeholder: 'Powers on, no power, untested' },
    { key: 'includedPower', label: 'Power/accessories', placeholder: 'Charger, cable, remote, battery' },
    { key: 'specs', label: 'Specs', placeholder: 'Storage, RAM, screen size, generation', multiline: true },
    { key: 'locksAccounts', label: 'Locks/accounts', placeholder: 'iCloud clear, BIOS locked, password unknown' },
  ],
  Electronics: [
    { key: 'serialNumber', label: 'Serial number', placeholder: 'Serial or service tag' },
    { key: 'powerStatus', label: 'Power status', placeholder: 'Powers on, untested, needs adapter' },
    { key: 'inputsTested', label: 'Inputs tested', placeholder: 'HDMI, USB, Bluetooth, Wi-Fi' },
    { key: 'accessories', label: 'Electronics accessories', placeholder: 'Remote, charger, manual' },
  ],
  Appliances: [
    { key: 'capacity', label: 'Capacity / size', placeholder: '5 qt, 1.7 cu ft, countertop' },
    { key: 'powerStatus', label: 'Power status', placeholder: 'Runs, heats, cools, untested' },
    { key: 'cleanliness', label: 'Cleanliness', placeholder: 'Clean, needs deep clean, odor' },
    { key: 'missingAccessories', label: 'Missing accessories', placeholder: 'Bowl, blade, tray, filter' },
  ],
  Furniture: [
    { key: 'dimensions', label: 'Dimensions', placeholder: 'W x D x H' },
    { key: 'material', label: 'Material', placeholder: 'Wood, veneer, metal, upholstery' },
    { key: 'finish', label: 'Finish / color', placeholder: 'Walnut, painted black, brass' },
    { key: 'stability', label: 'Stability', placeholder: 'Solid, wobbly, needs repair' },
    { key: 'pickupNotes', label: 'Pickup/storage notes', placeholder: 'Heavy, disassembles, truck needed' },
  ],
  Decor: [
    { key: 'dimensions', label: 'Dimensions', placeholder: 'Height, width, diameter' },
    { key: 'material', label: 'Material', placeholder: 'Ceramic, glass, brass, wood' },
    { key: 'styleEra', label: 'Style / era', placeholder: 'MCM, farmhouse, art deco, vintage' },
    { key: 'signatureMarks', label: 'Marks / signature', placeholder: 'Maker mark, stamp, label' },
  ],
  Tools: [
    { key: 'toolType', label: 'Tool type', placeholder: 'Cordless drill, socket set, saw' },
    { key: 'powerSource', label: 'Power source', placeholder: 'Battery, corded, pneumatic, manual' },
    { key: 'batteryPlatform', label: 'Battery platform', placeholder: 'M18, DeWalt 20V, Ryobi One+' },
    { key: 'testedFunction', label: 'Function tested', placeholder: 'Runs, charges, missing battery, untested' },
  ],
  Collectibles: [
    { key: 'franchise', label: 'Franchise / line', placeholder: 'Pokemon, Star Wars, Disney' },
    { key: 'releaseYear', label: 'Year / era', placeholder: '1999, 1980s, modern' },
    { key: 'authenticity', label: 'Authenticity notes', placeholder: 'COA, markings, reprint concern' },
    { key: 'packaging', label: 'Packaging', placeholder: 'Sealed, opened, box damage, loose' },
    { key: 'grading', label: 'Grade / grading candidate', placeholder: 'Raw, PSA 9, near mint' },
  ],
};
