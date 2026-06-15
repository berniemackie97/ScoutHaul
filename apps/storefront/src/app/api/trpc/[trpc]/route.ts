import { fetchRequestHandler } from "@trpc/server/adapters/fetch";
import { appRouter } from "@overhaul/api";
import { prisma } from "@overhaul/database";
import { auth } from "@clerk/nextjs/server";

const handler = async (req: Request) => {
  const { userId } = await auth();

  let userRole: string | null = null;
  if (userId) {
    const user = await prisma.user.findFirst({ where: { clerkId: userId } });
    userRole = user?.role ?? null;
  }

  return fetchRequestHandler({
    endpoint: "/api/trpc",
    req,
    router: appRouter,
    createContext: () => ({
      prisma,
      userId: userId ?? null,
      userRole,
    }),
  });
};

export { handler as GET, handler as POST };
