/** The HaulScout API this build syncs with. */
export const apiOrigin: string = __API_ORIGIN__;

/**
 * The portal addresses the bridge runs on, so a scan asked for there starts at once. The first is
 * where "Open HaulScout" goes until the bridge has seen which one is in use.
 */
export const portalOrigins: readonly string[] = __PORTAL_ORIGINS__;

/**
 * How long a signed-in check stays fresh before opening the popup checks again. Every scan checks
 * anyway; this only spares a check each time someone glances at the popup.
 */
export const signedInFreshMs = 30 * 60 * 1000;

/** How often working shows it is still alive. The popup stops showing work after three missed beats. */
export const aliveBeatMs = 20 * 1000;

/** How long a page read waits for something the page draws in after it loads, before reading what is there. */
export const untilWaitMs = 12 * 1000;

/** The least time between two checks of a sign-in that is not known to be good. */
export const recheckMs = 20 * 1000;

/**
 * How long after the operator's tab first shows them signed in before the one check that names the account — a
 * sign-in lands through redirects and token renewals, and a background tab joining in mid-way is what bot protection
 * looks for.
 */
export const signInSettleMs = 30 * 1000;

/** How long nothing is asked of a marketplace after the operator is seen on its sign-in form, or signed out. */
export const signInQuietMs = 2 * 60 * 1000;

/** The most activity entries the popup keeps. */
export const activityLimit = 30;

/** The same activity line again within this long is counted on the first rather than repeated. */
export const activityMergeMs = 30 * 60 * 1000;

/** How long a sync tab with nothing to do stays open for the next piece of work before it closes. */
export const syncTabIdleMs = 8 * 1000;

/** How long a marketplace page may take to load before the attempt is given up as unreachable. */
export const pageLoadTimeoutMs = 45 * 1000;
