import type { SaleChannelOption, SalesReferenceData } from './order.types';

export const fallbackChannelOptions: SaleChannelOption[] = [
  { value: 'Storefront', label: 'Own storefront', group: 'Owned channels', feeProfiles: null },
  { value: 'LocalPos', label: 'Local POS', group: 'Owned channels', feeProfiles: null },
  { value: 'eBay', label: 'eBay', group: 'Marketplaces', feeProfiles: null },
  { value: 'Depop', label: 'Depop', group: 'Marketplaces', feeProfiles: null },
  { value: 'Poshmark', label: 'Poshmark', group: 'Marketplaces', feeProfiles: null },
  { value: 'Mercari', label: 'Mercari', group: 'Marketplaces', feeProfiles: null },
  { value: 'Vinted', label: 'Vinted', group: 'Marketplaces', feeProfiles: null },
  { value: 'FacebookMarketplace', label: 'Facebook Marketplace', group: 'Marketplaces', feeProfiles: null },
  { value: 'Whatnot', label: 'Whatnot', group: 'Marketplaces', feeProfiles: null },
  { value: 'Etsy', label: 'Etsy', group: 'Marketplaces', feeProfiles: null },
  { value: 'Amazon', label: 'Amazon', group: 'Marketplaces', feeProfiles: null },
  { value: 'OfferUp', label: 'OfferUp', group: 'Marketplaces', feeProfiles: null },
  { value: 'Craigslist', label: 'Craigslist', group: 'Marketplaces', feeProfiles: null },
  { value: 'Auction', label: 'Auction', group: 'Offline / direct', feeProfiles: null },
  { value: 'Consignment', label: 'Consignment', group: 'Offline / direct', feeProfiles: null },
  { value: 'Wholesale', label: 'Wholesale', group: 'Offline / direct', feeProfiles: null },
  { value: 'Other', label: 'Other', group: 'Other', feeProfiles: null },
];

export function usableChannelOptions(referenceData: SalesReferenceData | null | undefined): SaleChannelOption[] {
  const controlledOptions = referenceData?.channels?.filter((option) => option.value.trim() && !option.isLegacy) ?? [];
  return controlledOptions.length > 0 ? controlledOptions : fallbackChannelOptions;
}
