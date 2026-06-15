import { fetchRequestHandler } from "@trpc/server/adapters/fetch";
import { appRouter } from "@overhaul/api";
import { prisma } from "@overhaul/database";
import { createSupabaseServerClient } from "@/lib/supabase/server";

// Transitional: this tRPC/Prisma layer is being replaced by the typed .NET API
// client (@overhaul/api-client). Auth is now Supabase; Clerk has been removed.
const handler = async (req: Request) => {
  const supabase = await createSupabaseServerClient();
  const {
    data: { user },
  } = await supabase.auth.getUser();

  return fetchRequestHandler({
    endpoint: "/api/trpc",
    req,
    router: appRouter,
    createContext: () => ({
      prisma,
      userId: user?.id ?? null,
      userRole: null,
    }),
  });
};

export { handler as GET, handler as POST };
