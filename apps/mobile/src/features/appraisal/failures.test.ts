import { ApiError } from '../../api/apiError';
import { describeFailure } from './failures';

const http = (status: number, code: string | null, message = 'Server said no.') =>
  new ApiError({ reason: 'http', status, code, message });

describe('describeFailure', () => {
  it('points an unknown barcode at a photo or typing it', () => {
    expect(describeFailure(http(404, 'barcode_unknown')).remedies).toEqual(['photo', 'details']);
  });

  it("keeps the server's own words when it could not identify the item", () => {
    const failure = describeFailure(http(404, 'not_identified', 'Retake the photo closer.'));

    expect(failure.message).toBe('Retake the photo closer.');
  });

  it('tells a spent quota apart from a busy service', () => {
    expect(describeFailure(http(429, 'quota_exhausted')).title).toBe('Daily limit reached');
    expect(describeFailure(http(429, 'rate_limited')).title).toBe('Identification is busy');
  });

  it('reads a bare 429 from the rate limiter as too many scans', () => {
    expect(describeFailure(http(429, null)).title).toBe('Slow down');
  });

  it('offers adding the item later when offline', () => {
    const failure = describeFailure(new ApiError({ reason: 'offline', message: 'offline' }));

    expect(failure.message).toContain('add the item now');
  });

  it('sends a signed-out person to sign in', () => {
    expect(describeFailure(new ApiError({ reason: 'unauthenticated', message: 'x' })).remedies).toEqual(['signIn']);
  });

  it('never shows a raw exception', () => {
    expect(describeFailure(new TypeError('undefined is not a function')).message).toBe('Something went wrong. Try again.');
  });
});
