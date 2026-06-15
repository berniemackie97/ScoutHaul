import { z } from "zod";
import { router, protectedProcedure, adminProcedure } from "../trpc";

export const customersRouter = router({
  profile: protectedProcedure.query(async ({ ctx }) => {
    return ctx.prisma.user.findUnique({
      where: { id: ctx.userId },
      include: { addresses: true },
    });
  }),

  updateProfile: protectedProcedure
    .input(
      z.object({
        firstName: z.string().optional(),
        lastName: z.string().optional(),
        phone: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.user.update({ where: { id: ctx.userId }, data: input });
    }),

  addAddress: protectedProcedure
    .input(
      z.object({
        label: z.string().optional(),
        line1: z.string(),
        line2: z.string().optional(),
        city: z.string(),
        state: z.string(),
        zipCode: z.string(),
        isDefault: z.boolean().default(false),
      })
    )
    .mutation(async ({ ctx, input }) => {
      if (input.isDefault) {
        await ctx.prisma.address.updateMany({
          where: { userId: ctx.userId },
          data: { isDefault: false },
        });
      }
      return ctx.prisma.address.create({
        data: { userId: ctx.userId, ...input },
      });
    }),

  wishlist: protectedProcedure.query(async ({ ctx }) => {
    return ctx.prisma.wishlistItem.findMany({
      where: { userId: ctx.userId },
      include: {
        product: {
          include: { images: { take: 1 }, category: true },
        },
      },
      orderBy: { createdAt: "desc" },
    });
  }),

  toggleWishlist: protectedProcedure
    .input(z.object({ productId: z.string() }))
    .mutation(async ({ ctx, input }) => {
      const existing = await ctx.prisma.wishlistItem.findUnique({
        where: { userId_productId: { userId: ctx.userId, productId: input.productId } },
      });

      if (existing) {
        await ctx.prisma.wishlistItem.delete({ where: { id: existing.id } });
        return { added: false };
      }

      await ctx.prisma.wishlistItem.create({
        data: { userId: ctx.userId, productId: input.productId },
      });
      return { added: true };
    }),

  notifications: protectedProcedure.query(async ({ ctx }) => {
    return ctx.prisma.notification.findMany({
      where: { userId: ctx.userId },
      orderBy: { createdAt: "desc" },
      take: 20,
    });
  }),

  markNotificationRead: protectedProcedure
    .input(z.object({ id: z.string() }))
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.notification.update({
        where: { id: input.id },
        data: { isRead: true },
      });
    }),

  // Admin CRM
  listAll: adminProcedure
    .input(
      z.object({
        cursor: z.string().optional(),
        limit: z.number().default(20),
        search: z.string().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = { role: "CUSTOMER" };
      if (input.search) {
        where.OR = [
          { email: { contains: input.search, mode: "insensitive" } },
          { firstName: { contains: input.search, mode: "insensitive" } },
          { lastName: { contains: input.search, mode: "insensitive" } },
        ];
      }

      const items = await ctx.prisma.user.findMany({
        where: where as any,
        take: input.limit + 1,
        cursor: input.cursor ? { id: input.cursor } : undefined,
        orderBy: { createdAt: "desc" },
        include: {
          _count: { select: { orders: true, repairJobs: true } },
        },
      });

      let nextCursor: string | undefined;
      if (items.length > input.limit) {
        nextCursor = items.pop()?.id;
      }

      return { items, nextCursor };
    }),

  customerDetail: adminProcedure
    .input(z.object({ id: z.string() }))
    .query(async ({ ctx, input }) => {
      return ctx.prisma.user.findUnique({
        where: { id: input.id },
        include: {
          orders: { orderBy: { createdAt: "desc" }, take: 10, include: { items: true } },
          repairJobs: { orderBy: { createdAt: "desc" }, take: 5 },
          consignments: { orderBy: { createdAt: "desc" }, take: 5 },
          addresses: true,
        },
      });
    }),
});
