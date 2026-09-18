import { forwardFiles } from "@/lib/import-proxy";

/** Imports the dropped files, each as its own sync run. */
export async function POST(request: Request) {
  return forwardFiles(request, "/api/imports");
}
