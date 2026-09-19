import { describe, expect, it } from "vitest";
import { describeBrowser } from "./environment";

describe("naming this browser", () => {
  it("recognises the browser and the system from the user agent", () => {
    expect(describeBrowser("Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:142.0) Gecko/20100101 Firefox/142.0"))
      .toEqual({ browser: "Firefox", operatingSystem: "macOS" });
    expect(describeBrowser("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Edg/140.0"))
      .toEqual({ browser: "Edge", operatingSystem: "Windows" });
    expect(describeBrowser("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36"))
      .toEqual({ browser: "Chrome", operatingSystem: "Linux" });
  });
});
