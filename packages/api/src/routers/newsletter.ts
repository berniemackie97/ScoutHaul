import { z } from "zod";
import { router, publicProcedure, adminProcedure } from "../trpc";

export const newsletterRouter = router({
  subscribe: publicProcedure
    .input(z.object({ email: z.string().email(), firstName: z.string().optional() }))
    .mutation(async ({ ctx, input }) => {
      const existing = await ctx.prisma.newsletterSubscriber.findUnique({
        where: { email: input.email },
      });

      if (existing) {
        if (!existing.isActive) {
          await ctx.prisma.newsletterSubscriber.update({
            where: { id: existing.id },
            data: { isActive: true, unsubscribedAt: null },
          });
        }
        return { success: true };
      }

      await ctx.prisma.newsletterSubscriber.create({ data: input });
      return { success: true };
    }),

  unsubscribe: publicProcedure
    .input(z.object({ email: z.string().email() }))
    .mutation(async ({ ctx, input }) => {
      await ctx.prisma.newsletterSubscriber.updateMany({
        where: { email: input.email },
        data: { isActive: false, unsubscribedAt: new Date() },
      });
      return { success: true };
    }),

  listSubscribers: adminProcedure.query(async ({ ctx }) => {
    return ctx.prisma.newsletterSubscriber.findMany({
      where: { isActive: true },
      orderBy: { subscribedAt: "desc" },
    });
  }),
});
