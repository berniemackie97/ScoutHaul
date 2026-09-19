/** Which browser and system this is, for naming the device when it pairs. */
export function describeBrowser(userAgent: string = navigator.userAgent): { browser: string; operatingSystem: string | null } {
  const browser = /Firefox\//.test(userAgent)
    ? "Firefox"
    : /Edg\//.test(userAgent)
      ? "Edge"
      : /Chrome\//.test(userAgent)
        ? "Chrome"
        : /Safari\//.test(userAgent)
          ? "Safari"
          : "Browser";
  const operatingSystem = /Mac OS X|Macintosh/.test(userAgent)
    ? "macOS"
    : /Windows/.test(userAgent)
      ? "Windows"
      : /CrOS/.test(userAgent)
        ? "ChromeOS"
        : /Linux/.test(userAgent)
          ? "Linux"
          : null;
  return { browser, operatingSystem };
}
