import { apiRequest, uploadMultipart } from '../../api/client';
import type { Appraisal, AppraiseRequest, BuyCheck, BuyCheckRequest, Identification } from './types';

// Identification can run a vision model with retries; pricing waits on a web search with a 45 s
// server deadline. Both need more than the client's default.
const IDENTIFY_TIMEOUT_MS = 45_000;
const APPRAISE_TIMEOUT_MS = 60_000;

export type IdentifyInput = {
  photoUri?: string;
  barcode?: string;
  details?: string;
};

export function identify(input: IdentifyInput, signal?: AbortSignal): Promise<Identification> {
  const form = new FormData();
  if (input.photoUri) {
    form.append('photo', { uri: input.photoUri, name: 'item.jpg', type: 'image/jpeg' } as unknown as Blob);
  }
  if (input.barcode) form.append('barcode', input.barcode);
  if (input.details) form.append('details', input.details);

  return uploadMultipart<Identification>('/api/appraisals/identify', form, undefined, {
    signal,
    timeoutMs: IDENTIFY_TIMEOUT_MS,
  });
}

export function appraise(request: AppraiseRequest, signal?: AbortSignal): Promise<Appraisal> {
  return apiRequest<Appraisal>('/api/appraisals', {
    method: 'POST',
    body: request,
    signal,
    timeoutMs: APPRAISE_TIMEOUT_MS,
  });
}

export function checkBuy(request: BuyCheckRequest, signal?: AbortSignal): Promise<BuyCheck> {
  return apiRequest<BuyCheck>('/api/buy-checks', { method: 'POST', body: request, signal });
}

export function toAppraiseRequest(item: Identification): AppraiseRequest {
  return {
    title: item.title,
    brand: item.brand,
    model: item.model,
    category: item.category,
    color: item.color,
    condition: item.condition,
    barcode: item.barcode,
    identifiers: item.identifiers,
    certainty: item.certainty,
    productIdentityId: item.productIdentityId,
    estimate: item.estimate,
  };
}
