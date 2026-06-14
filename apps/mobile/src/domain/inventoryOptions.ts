import type { Ionicons } from '@expo/vector-icons';
import type { SelectOption } from '../components/SelectField';
import type { InventoryStatus } from './types';

export const conditionOptions: SelectOption[] = [
  { value: 'New with tags', label: 'New with tags', description: 'Original tags attached, never used', icon: 'pricetag-outline' },
  { value: 'New without tags', label: 'New without tags', description: 'Unused but tags removed or missing', icon: 'pricetag-outline' },
  { value: 'Open box', label: 'Open box', description: 'Opened but never used, all contents present', icon: 'cube-outline' },
  { value: 'Near mint', label: 'Near mint', description: 'Minimal signs of use, excellent condition', icon: 'star-outline' },
  { value: 'Light use', label: 'Light use', description: 'Minor wear, fully functional', icon: 'checkmark-circle-outline' },
  { value: 'Moderate use', label: 'Moderate use', description: 'Visible wear, works correctly', icon: 'remove-circle-outline' },
  { value: 'Heavy use', label: 'Heavy use', description: 'Significant wear, may have cosmetic issues', icon: 'warning-outline' },
  { value: 'Damaged', label: 'Damaged', description: 'Functional or cosmetic damage present', icon: 'alert-circle-outline' },
  { value: 'For parts', label: 'For parts / not working', description: 'Non-functional, sold for components', icon: 'build-outline' },
];

export const categoryOptions: SelectOption[] = [
  { value: 'Clothing', label: 'Clothing', icon: 'shirt-outline', group: 'Apparel' },
  { value: 'Shoes', label: 'Shoes', icon: 'footsteps-outline', group: 'Apparel' },
  { value: 'Tech', label: 'Tech & Computers', icon: 'laptop-outline', group: 'Electronics' },
  { value: 'Electronics', label: 'Electronics', icon: 'radio-outline', group: 'Electronics' },
  { value: 'Appliances', label: 'Appliances', icon: 'cafe-outline', group: 'Electronics' },
  { value: 'Video Games', label: 'Video Games', icon: 'game-controller-outline', group: 'Entertainment' },
  { value: 'Toys', label: 'Toys & Games', icon: 'dice-outline', group: 'Entertainment' },
  { value: 'Collectibles', label: 'Collectibles', icon: 'trophy-outline', group: 'Entertainment' },
  { value: 'Books', label: 'Books', icon: 'book-outline', group: 'Entertainment' },
  { value: 'Media', label: 'Media (DVD, Vinyl, etc.)', icon: 'disc-outline', group: 'Entertainment' },
  { value: 'Furniture', label: 'Furniture', icon: 'bed-outline', group: 'Home' },
  { value: 'Decor', label: 'Decor & Art', icon: 'color-palette-outline', group: 'Home' },
  { value: 'Home', label: 'Home & Kitchen', icon: 'home-outline', group: 'Home' },
  { value: 'Tools', label: 'Tools & Hardware', icon: 'hammer-outline', group: 'Home' },
  { value: 'Other', label: 'Other', icon: 'ellipsis-horizontal-outline', group: 'Other' },
];

export const sizeSystemOptions: SelectOption[] = [
  { value: 'US letter', label: 'US Letter (S/M/L/XL)', description: 'Standard letter sizing' },
  { value: 'US numeric', label: 'US Numeric (0–20)', description: 'Numeric dress/pant sizing' },
  { value: 'Waist/Inseam', label: 'Waist × Inseam', description: 'e.g., 32×30' },
  { value: 'Shoe US M', label: 'Shoe US Men\'s', description: 'Men\'s shoe sizing' },
  { value: 'Shoe US W', label: 'Shoe US Women\'s', description: 'Women\'s shoe sizing' },
  { value: 'Youth', label: 'Youth / Kids', description: 'Children\'s sizing' },
  { value: 'One size', label: 'One Size', description: 'Universal fit' },
  { value: 'Other', label: 'Other', description: 'Custom or international sizing' },
];

export const damageSeverityOptions: SelectOption[] = [
  { value: 'Cosmetic', label: 'Cosmetic', description: 'Visual only, does not affect function', icon: 'eye-outline' },
  { value: 'Minor', label: 'Minor', description: 'Small issue, still fully usable', icon: 'alert-outline' },
  { value: 'Moderate', label: 'Moderate', description: 'Noticeable issue, may affect value', icon: 'warning-outline' },
  { value: 'Major', label: 'Major', description: 'Significant damage, limited use', icon: 'alert-circle-outline' },
  { value: 'Parts only', label: 'Parts Only', description: 'Non-functional, salvage value only', icon: 'build-outline' },
];

export const damageTypeOptions: SelectOption[] = [
  { value: 'Stain', label: 'Stain', icon: 'water-outline' },
  { value: 'Hole/tear', label: 'Hole / Tear', icon: 'cut-outline' },
  { value: 'Scuff/scratch', label: 'Scuff / Scratch', icon: 'brush-outline' },
  { value: 'Missing part', label: 'Missing Part', icon: 'remove-circle-outline' },
  { value: 'Odor', label: 'Odor', icon: 'cloud-outline' },
  { value: 'Does not power on', label: 'Does Not Power On', icon: 'flash-off-outline' },
  { value: 'Untested', label: 'Untested', icon: 'help-circle-outline' },
  { value: 'Other', label: 'Other', icon: 'ellipsis-horizontal-outline' },
];

export const workflowActionOptions: { status: InventoryStatus; label: string }[] = [
  { status: 'NeedsCleaning', label: 'Needs cleaning' },
  { status: 'NeedsTesting', label: 'Needs testing' },
  { status: 'NeedsRepair', label: 'Needs repair / alterations' },
  { status: 'NeedsPhotos', label: 'Needs photos' },
  { status: 'ReadyToList', label: 'Ready to list' },
  { status: 'Listed', label: 'Listed' },
];

export type InventoryAttributeField = {
  key: string;
  label: string;
  placeholder: string;
  multiline?: boolean;
  type?: 'text' | 'select';
  options?: SelectOption[];
};

export const categoryAttributeFields: Record<string, InventoryAttributeField[]> = {
  Clothing: [
    { key: 'material', label: 'Material / fabric', placeholder: 'Cotton, wool, polyester blend' },
    { key: 'materialPercentages', label: 'Material percentages', placeholder: '80% cotton, 20% polyester' },
    {
      key: 'genderFit', label: 'Fit / department', placeholder: 'Select fit', type: 'select',
      options: [
        { value: 'Men', label: 'Men\'s' },
        { value: 'Women', label: 'Women\'s' },
        { value: 'Youth', label: 'Youth / Kids' },
        { value: 'Unisex', label: 'Unisex' },
        { value: 'Slim', label: 'Slim Fit' },
        { value: 'Relaxed', label: 'Relaxed Fit' },
        { value: 'Plus', label: 'Plus Size' },
      ],
    },
    { key: 'measurements', label: 'Measurements', placeholder: 'Chest, length, sleeve, waist', multiline: true },
    { key: 'care', label: 'Care notes', placeholder: 'Machine wash, dry clean only' },
  ],
  Shoes: [
    { key: 'shoeSize', label: 'Shoe size', placeholder: 'US 10.5 M, EU 44' },
    {
      key: 'width', label: 'Width', placeholder: 'Select width', type: 'select',
      options: [
        { value: 'Narrow', label: 'Narrow (B)' },
        { value: 'Standard', label: 'Standard (D)' },
        { value: 'Wide', label: 'Wide (EE)' },
        { value: 'Extra Wide', label: 'Extra Wide (EEE+)' },
      ],
    },
    { key: 'upperMaterial', label: 'Upper material', placeholder: 'Leather, suede, mesh' },
    {
      key: 'soleCondition', label: 'Sole condition', placeholder: 'Select condition', type: 'select',
      options: [
        { value: 'Unworn', label: 'Unworn', description: 'No tread wear' },
        { value: '90%+', label: '90%+ tread', description: 'Barely worn' },
        { value: '70-90%', label: '70–90% tread', description: 'Light wear' },
        { value: '50-70%', label: '50–70% tread', description: 'Moderate wear' },
        { value: '<50%', label: 'Under 50%', description: 'Heavy wear' },
        { value: 'Separation', label: 'Sole separation', description: 'Needs repair' },
      ],
    },
  ],
  Tech: [
    { key: 'serialNumber', label: 'Serial / IMEI', placeholder: 'Serial, IMEI, service tag' },
    {
      key: 'powerStatus', label: 'Power status', placeholder: 'Select status', type: 'select',
      options: [
        { value: 'Powers on', label: 'Powers on', icon: 'checkmark-circle-outline' },
        { value: 'Powers on with issues', label: 'Powers on with issues', icon: 'warning-outline' },
        { value: 'No power', label: 'No power', icon: 'flash-off-outline' },
        { value: 'Untested', label: 'Untested', icon: 'help-circle-outline' },
      ],
    },
    { key: 'includedPower', label: 'Power/accessories', placeholder: 'Charger, cable, remote, battery' },
    { key: 'specs', label: 'Specs', placeholder: 'Storage, RAM, screen size, generation', multiline: true },
    {
      key: 'locksAccounts', label: 'Locks/accounts', placeholder: 'Select status', type: 'select',
      options: [
        { value: 'Clear', label: 'All clear', description: 'No locks, accounts removed' },
        { value: 'iCloud locked', label: 'iCloud locked', description: 'Apple activation lock present' },
        { value: 'Google locked', label: 'Google FRP locked', description: 'Factory reset protection active' },
        { value: 'BIOS locked', label: 'BIOS/firmware locked', description: 'Password required at boot' },
        { value: 'Unknown', label: 'Unknown', description: 'Not yet checked' },
      ],
    },
  ],
  Electronics: [
    { key: 'serialNumber', label: 'Serial number', placeholder: 'Serial or service tag' },
    {
      key: 'powerStatus', label: 'Power status', placeholder: 'Select status', type: 'select',
      options: [
        { value: 'Powers on', label: 'Powers on', icon: 'checkmark-circle-outline' },
        { value: 'Powers on with issues', label: 'Powers on with issues', icon: 'warning-outline' },
        { value: 'Needs adapter', label: 'Needs adapter', icon: 'battery-charging-outline' },
        { value: 'Untested', label: 'Untested', icon: 'help-circle-outline' },
      ],
    },
    { key: 'inputsTested', label: 'Inputs tested', placeholder: 'HDMI, USB, Bluetooth, Wi-Fi' },
    { key: 'accessories', label: 'Electronics accessories', placeholder: 'Remote, charger, manual' },
  ],
  Appliances: [
    { key: 'capacity', label: 'Capacity / size', placeholder: '5 qt, 1.7 cu ft, countertop' },
    {
      key: 'powerStatus', label: 'Power status', placeholder: 'Select status', type: 'select',
      options: [
        { value: 'Runs', label: 'Runs normally', icon: 'checkmark-circle-outline' },
        { value: 'Heats', label: 'Heats properly', icon: 'flame-outline' },
        { value: 'Cools', label: 'Cools properly', icon: 'snow-outline' },
        { value: 'Untested', label: 'Untested', icon: 'help-circle-outline' },
      ],
    },
    {
      key: 'cleanliness', label: 'Cleanliness', placeholder: 'Select', type: 'select',
      options: [
        { value: 'Clean', label: 'Clean', description: 'Ready to sell' },
        { value: 'Needs cleaning', label: 'Needs cleaning', description: 'Surface cleaning needed' },
        { value: 'Deep clean', label: 'Needs deep clean', description: 'Significant buildup' },
        { value: 'Odor', label: 'Odor present', description: 'Smell treatment needed' },
      ],
    },
    { key: 'missingAccessories', label: 'Missing accessories', placeholder: 'Bowl, blade, tray, filter' },
  ],
  Furniture: [
    { key: 'dimensions', label: 'Dimensions', placeholder: 'W × D × H in inches' },
    { key: 'material', label: 'Material', placeholder: 'Wood, veneer, metal, upholstery' },
    { key: 'finish', label: 'Finish / color', placeholder: 'Walnut, painted black, brass' },
    {
      key: 'stability', label: 'Stability', placeholder: 'Select', type: 'select',
      options: [
        { value: 'Solid', label: 'Solid', description: 'No wobble, structurally sound' },
        { value: 'Minor wobble', label: 'Minor wobble', description: 'Slight movement, usable' },
        { value: 'Needs repair', label: 'Needs repair', description: 'Structural issue present' },
      ],
    },
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
    {
      key: 'powerSource', label: 'Power source', placeholder: 'Select', type: 'select',
      options: [
        { value: 'Battery', label: 'Battery / Cordless', icon: 'battery-full-outline' },
        { value: 'Corded', label: 'Corded / AC', icon: 'flash-outline' },
        { value: 'Pneumatic', label: 'Pneumatic / Air', icon: 'cloud-outline' },
        { value: 'Manual', label: 'Manual / Hand', icon: 'hand-left-outline' },
        { value: 'Gas', label: 'Gas powered', icon: 'flame-outline' },
      ],
    },
    { key: 'batteryPlatform', label: 'Battery platform', placeholder: 'M18, DeWalt 20V, Ryobi One+' },
    {
      key: 'testedFunction', label: 'Function tested', placeholder: 'Select', type: 'select',
      options: [
        { value: 'Runs', label: 'Runs / works', icon: 'checkmark-circle-outline' },
        { value: 'Charges', label: 'Battery charges', icon: 'battery-charging-outline' },
        { value: 'Missing battery', label: 'Missing battery', icon: 'battery-dead-outline' },
        { value: 'Untested', label: 'Untested', icon: 'help-circle-outline' },
      ],
    },
  ],
  Collectibles: [
    { key: 'franchise', label: 'Franchise / line', placeholder: 'Pokemon, Star Wars, Disney' },
    { key: 'releaseYear', label: 'Year / era', placeholder: '1999, 1980s, modern' },
    { key: 'authenticity', label: 'Authenticity notes', placeholder: 'COA, markings, reprint concern' },
    {
      key: 'packaging', label: 'Packaging', placeholder: 'Select', type: 'select',
      options: [
        { value: 'Sealed', label: 'Factory sealed', description: 'Original shrink wrap intact' },
        { value: 'Opened', label: 'Opened / complete', description: 'All contents present' },
        { value: 'Box damage', label: 'Box damage', description: 'Contents fine, box shows wear' },
        { value: 'Loose', label: 'Loose / no box', description: 'No original packaging' },
      ],
    },
    { key: 'grading', label: 'Grade / grading candidate', placeholder: 'Raw, PSA 9, near mint' },
  ],
};
