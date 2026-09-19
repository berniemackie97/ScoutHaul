/** The HaulScout API this build syncs with. */
export const apiOrigin: string = __API_ORIGIN__;

/** The portal whose pages the bridge runs on. */
export const portalOrigin: string = __PORTAL_ORIGIN__;

/** How long a marketplace sign-in check stays fresh before the popup asks again. */
export const sessionFreshMs = 2 * 60 * 1000;

/** The most activity entries the popup keeps. */
export const activityLimit = 30;
