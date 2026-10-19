import { ApiError } from '../../api/apiError';

/** What the person should do next after a scan fails. */
export type FailureRemedy = 'retry' | 'photo' | 'details' | 'wait' | 'signIn';

export type ScanFailure = {
  title: string;
  message: string;
  remedies: FailureRemedy[];
};

export function describeFailure(error: unknown): ScanFailure {
  if (!(error instanceof ApiError)) {
    return { title: "Couldn't finish", message: 'Something went wrong. Try again.', remedies: ['retry'] };
  }

  switch (error.code) {
    case 'barcode_unknown':
      return {
        title: 'Barcode not found',
        message: "This barcode isn't in any registry we can reach. Take a photo or type what it is.",
        remedies: ['photo', 'details'],
      };
    case 'not_identified':
      return { title: "Couldn't tell what this is", message: error.message, remedies: ['photo', 'details'] };
    case 'quota_exhausted':
      return {
        title: 'Daily limit reached',
        message: "Photo identification has used today's quota. Scan the barcode or type what it is instead.",
        remedies: ['details'],
      };
    case 'rate_limited':
    case 'upstream_unavailable':
      return { title: 'Identification is busy', message: 'Try again in a moment.', remedies: ['wait', 'details'] };
    case 'vision_not_configured':
      return {
        title: 'Photo identification is off',
        message: 'The server has no vision key set. Scan the barcode instead.',
        remedies: [],
      };
    case 'photo_unsupported':
    case 'photo_too_large':
      return { title: "That photo didn't work", message: error.message, remedies: ['photo'] };
  }

  switch (error.reason) {
    case 'offline':
      return {
        title: "You're offline",
        message: 'Identifying and pricing need a connection. You can still add the item now and price it later.',
        remedies: ['retry'],
      };
    case 'timeout':
      return { title: 'Took too long', message: 'The connection is slow. Try again.', remedies: ['retry'] };
    case 'unauthenticated':
      return { title: 'Signed out', message: 'Sign in again to scan.', remedies: ['signIn'] };
    case 'http':
      if (error.status === 429) {
        return { title: 'Slow down', message: 'That was a lot of scans in a minute. Wait a moment.', remedies: ['wait'] };
      }
      return { title: "Couldn't finish", message: error.message, remedies: ['retry'] };
    default:
      return { title: "Couldn't reach the server", message: error.message, remedies: ['retry'] };
  }
}
