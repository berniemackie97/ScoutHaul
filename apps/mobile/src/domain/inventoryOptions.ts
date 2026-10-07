import type { SelectOption } from '../components/SelectField';
import type { InventoryStatus } from './types';

export const manualInventoryStatusOptions: SelectOption[] = [
  { value: 'Draft', label: 'Draft', description: 'Captured but not operationally ready', group: 'Active' },
  { value: 'Researching', label: 'Researching', description: 'Needs identity, comps, or listing research', group: 'Active' },
  { value: 'Watchlist', label: 'Watchlist', description: 'Tracked but not yet purchased/committed', group: 'Active' },
  { value: 'Purchased', label: 'Purchased', description: 'Owned/acquired and waiting for intake work', group: 'Active' },
  { value: 'NeedsCleaning', label: 'Needs cleaning', description: 'Physical cleanup is required before listing', group: 'Prep' },
  { value: 'NeedsTesting', label: 'Needs testing', description: 'Function must be tested or disclosed', group: 'Prep' },
  { value: 'NeedsRepair', label: 'Needs repair', description: 'Repair or parts-only decision is required', group: 'Prep' },
  { value: 'NeedsPhotos', label: 'Needs photos', description: 'Photography is required before listing', group: 'Prep' },
  { value: 'ReadyToList', label: 'Ready to list', description: 'Facts, condition, valuation, and prep are ready', group: 'Listing' },
  { value: 'Listed', label: 'Listed', description: 'Published on at least one sales channel', group: 'Listing' },
  { value: 'Donated', label: 'Donated', description: 'Removed from active resale inventory', group: 'Closed' },
  { value: 'Disposed', label: 'Disposed', description: 'Removed from active inventory as waste/loss', group: 'Closed' },
];

export const createInventoryStatusOptions = manualInventoryStatusOptions.filter(
  (option) => !['Listed', 'Donated', 'Disposed'].includes(option.value),
);

export const commerceManagedStatuses: InventoryStatus[] = ['Sold', 'Packed', 'Shipped', 'Delivered', 'Returned'];
export const terminalInventoryStatuses: InventoryStatus[] = ['Donated', 'Disposed'];

export function inventoryStatusLabel(value?: string | null): string {
  if (!value) return '—';
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

export const ownershipOptions: SelectOption[] = [
  { value: 'Owned', label: 'Owned inventory' },
  { value: 'Consigned', label: 'Consigned inventory' },
  { value: 'CustomerRepair', label: 'Customer-owned repair' },
  { value: 'VendorManaged', label: 'Vendor-managed inventory' },
];

export const functionalStatusOptions: SelectOption[] = [
  { value: 'Unknown', label: 'Unknown' },
  { value: 'Untested', label: 'Untested' },
  { value: 'FullyFunctional', label: 'Fully functional' },
  { value: 'FunctionalWithIssues', label: 'Functional with issues' },
  { value: 'Intermittent', label: 'Intermittent' },
  { value: 'NonFunctional', label: 'Non-functional' },
  { value: 'PartsOnly', label: 'Parts only' },
];

export const completenessStatusOptions: SelectOption[] = [
  { value: 'Unknown', label: 'Unknown / not recorded' },
  { value: 'Complete', label: 'Complete' },
  { value: 'MissingAccessories', label: 'Missing accessories' },
  { value: 'MissingParts', label: 'Missing parts' },
  { value: 'Incomplete', label: 'Incomplete set' },
];

export const authenticityStatusOptions: SelectOption[] = [
  { value: 'NotAssessed', label: 'Not assessed' },
  { value: 'Authentic', label: 'Authenticated' },
  { value: 'LikelyAuthentic', label: 'Likely authentic' },
  { value: 'NeedsVerification', label: 'Needs verification' },
  { value: 'Replica', label: 'Replica (disclosed)' },
  { value: 'Counterfeit', label: 'Counterfeit — do not list' },
];

export const packagingStatusOptions: SelectOption[] = [
  { value: 'NotApplicable', label: 'Not applicable' },
  { value: 'FactorySealed', label: 'Factory sealed' },
  { value: 'OriginalExcellent', label: 'Original packaging — excellent' },
  { value: 'OriginalWorn', label: 'Original packaging — worn' },
  { value: 'OriginalDamaged', label: 'Original packaging — damaged' },
  { value: 'ReplacementPackaging', label: 'Replacement packaging' },
  { value: 'LooseNoPackaging', label: 'Loose / no packaging' },
];

export const dimensionUnitOptions: SelectOption[] = [
  { value: 'Inches', label: 'Inches' },
  { value: 'Centimeters', label: 'Centimeters' },
];

export const weightUnitOptions: SelectOption[] = [
  { value: 'Ounces', label: 'Ounces' },
  { value: 'Pounds', label: 'Pounds' },
  { value: 'Grams', label: 'Grams' },
  { value: 'Kilograms', label: 'Kilograms' },
];

export const conditionOptions: SelectOption[] = [
  { value: 'Factory sealed', label: 'Factory sealed', description: 'Original factory seal is intact', icon: 'lock-closed-outline' },
  { value: 'New', label: 'New', description: 'Unused and complete', icon: 'sparkles-outline' },
  { value: 'New with tags', label: 'New with tags', description: 'Original tags attached, never used', icon: 'pricetag-outline' },
  { value: 'New without tags', label: 'New without tags', description: 'Unused but tags removed or missing', icon: 'pricetag-outline' },
  { value: 'Open box', label: 'Open box', description: 'Opened but never used, all contents present', icon: 'cube-outline' },
  { value: 'Refurbished', label: 'Refurbished', description: 'Restored, tested, and disclosed as refurbished', icon: 'construct-outline' },
  { value: 'Excellent', label: 'Excellent', description: 'Minimal wear and exceptional presentation', icon: 'star-outline' },
  { value: 'Good', label: 'Good', description: 'Normal light wear with strong presentation', icon: 'checkmark-circle-outline' },
  { value: 'Fair', label: 'Fair', description: 'Visible wear; defects are fully disclosed', icon: 'remove-circle-outline' },
  { value: 'Poor', label: 'Poor', description: 'Heavy wear or material cosmetic damage', icon: 'warning-outline' },
  { value: 'For parts', label: 'For parts / not working', description: 'Non-functional, sold for components', icon: 'build-outline' },
];

export const categoryOptions: SelectOption[] = [
  { value: 'Clothing', label: 'Clothing', icon: 'shirt-outline', group: 'Apparel' },
  { value: 'Shoes', label: 'Shoes', icon: 'footsteps-outline', group: 'Apparel' },
  { value: 'Handbags', label: 'Handbags & Accessories', icon: 'bag-handle-outline', group: 'Apparel' },
  { value: 'Jewelry', label: 'Jewelry', icon: 'diamond-outline', group: 'Apparel' },
  { value: 'Watches', label: 'Watches', icon: 'watch-outline', group: 'Apparel' },
  { value: 'Tech', label: 'Tech & Computers', icon: 'laptop-outline', group: 'Electronics' },
  { value: 'Phones', label: 'Cell Phones & Accessories', icon: 'phone-portrait-outline', group: 'Electronics' },
  { value: 'Cameras', label: 'Cameras & Photography', icon: 'camera-outline', group: 'Electronics' },
  { value: 'Audio', label: 'Audio & Headphones', icon: 'headset-outline', group: 'Electronics' },
  { value: 'TV', label: 'TV & Home Theater', icon: 'tv-outline', group: 'Electronics' },
  { value: 'Electronics', label: 'Electronics', icon: 'radio-outline', group: 'Electronics' },
  { value: 'Appliances', label: 'Appliances', icon: 'cafe-outline', group: 'Electronics' },
  { value: 'Video Games', label: 'Video Games', icon: 'game-controller-outline', group: 'Entertainment' },
  { value: 'Toys', label: 'Toys & Games', icon: 'dice-outline', group: 'Entertainment' },
  { value: 'Collectibles', label: 'Collectibles', icon: 'trophy-outline', group: 'Entertainment' },
  { value: 'Books', label: 'Books', icon: 'book-outline', group: 'Entertainment' },
  { value: 'Media', label: 'Media (DVD, Vinyl, etc.)', icon: 'disc-outline', group: 'Entertainment' },
  { value: 'Musical Instruments', label: 'Musical Instruments', icon: 'musical-notes-outline', group: 'Entertainment' },
  { value: 'Furniture', label: 'Furniture', icon: 'bed-outline', group: 'Home' },
  { value: 'Decor', label: 'Decor & Art', icon: 'color-palette-outline', group: 'Home' },
  { value: 'Home', label: 'Home & Kitchen', icon: 'home-outline', group: 'Home' },
  { value: 'Tools', label: 'Tools & Hardware', icon: 'hammer-outline', group: 'Home' },
  { value: 'Garden', label: 'Garden & Outdoor', icon: 'leaf-outline', group: 'Home' },
  { value: 'Office', label: 'Office Equipment & Supplies', icon: 'briefcase-outline', group: 'Home' },
  { value: 'Crafts', label: 'Crafts & Hobbies', icon: 'color-palette-outline', group: 'Home' },
  { value: 'Sporting Goods', label: 'Sporting Goods', icon: 'football-outline', group: 'Lifestyle' },
  { value: 'Fitness', label: 'Fitness Equipment', icon: 'barbell-outline', group: 'Lifestyle' },
  { value: 'Health & Beauty', label: 'Health & Beauty', icon: 'sparkles-outline', group: 'Lifestyle' },
  { value: 'Baby', label: 'Baby', icon: 'happy-outline', group: 'Lifestyle' },
  { value: 'Pet Supplies', label: 'Pet Supplies', icon: 'paw-outline', group: 'Lifestyle' },
  { value: 'Automotive', label: 'Automotive', icon: 'car-outline', group: 'Automotive & Industrial' },
  { value: 'Industrial', label: 'Business & Industrial', icon: 'business-outline', group: 'Automotive & Industrial' },
  { value: 'Other', label: 'Other', icon: 'ellipsis-horizontal-outline', group: 'Other' },
];

export const sizeSystemOptions: SelectOption[] = [
  { value: 'US letter', label: 'US Letter (S/M/L/XL)', description: 'Standard letter sizing' },
  { value: 'US numeric', label: 'US Numeric (0–20)', description: 'Numeric dress/pant sizing' },
  { value: 'Waist/Inseam', label: 'Waist × Inseam', description: 'e.g., 32×30' },
  { value: 'Shoe US M', label: 'Shoe US Men\'s', description: 'Men\'s shoe sizing' },
  { value: 'Shoe US W', label: 'Shoe US Women\'s', description: 'Women\'s shoe sizing' },
  { value: 'Shoe US Youth', label: 'Shoe US Youth', description: 'Youth shoe sizing' },
  { value: 'Shoe EU', label: 'Shoe EU', description: 'European shoe sizing' },
  { value: 'Shoe UK', label: 'Shoe UK', description: 'United Kingdom shoe sizing' },
  { value: 'Youth', label: 'Youth / Kids', description: 'Children\'s sizing' },
  { value: 'One size', label: 'One Size', description: 'Universal fit' },
];

export const colorOptions: SelectOption[] = [
  'Black', 'White', 'Gray', 'Silver', 'Red', 'Orange', 'Yellow', 'Green', 'Blue', 'Purple',
  'Pink', 'Brown', 'Beige', 'Cream', 'Navy', 'Teal', 'Maroon', 'Gold', 'Rose gold', 'Bronze',
  'Natural', 'Multicolor', 'Patterned', 'Clear', 'Other',
].map((value) => ({ value, label: value }));

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
