import { forwardFiles } from "@/lib/import-proxy";

/** What each dropped file is, before anything is imported. */
export async function POST(request: Request) {
  return forwardFiles(request, "/api/imports/detect");
}
