import { z } from "zod";
import { router, adminProcedure } from "../trpc";

export const suppliersRouter = router({
  list: adminProcedure
    .input(z.object({ type: z.string().optional(), isActive: z.boolean().optional() }))
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = {};
      if (input.type) where.type = input.type;
      if (input.isActive !== undefined) where.isActive = input.isActive;

      return ctx.prisma.supplier.findMany({
        where: where as any,
        orderBy: { name: "asc" },
        include: {
          _count: { select: { products: true, purchases: true } },
        },
      });
    }),

  byId: adminProcedure
    .input(z.object({ id: z.string() }))
    .query(async ({ ctx, input }) => {
      return ctx.prisma.supplier.findUnique({
        where: { id: input.id },
        include: {
          products: { take: 20, orderBy: { createdAt: "desc" }, include: { images: { take: 1 } } },
          purchases: { orderBy: { purchaseDate: "desc" }, take: 10 },
        },
      });
    }),

  create: adminProcedure
    .input(
      z.object({
        name: z.string().min(1),
        type: z.string(),
        contactName: z.string().optional(),
        email: z.string().email().optional(),
        phone: z.string().optional(),
        address: z.string().optional(),
        notes: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.supplier.create({ data: input as any });
    }),

  update: adminProcedure
    .input(
      z.object({
        id: z.string(),
        name: z.string().optional(),
        contactName: z.string().optional(),
        email: z.string().optional(),
        phone: z.string().optional(),
        notes: z.string().optional(),
        isActive: z.boolean().optional(),
        rating: z.number().min(1).max(5).optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const { id, ...data } = input;
      return ctx.prisma.supplier.update({ where: { id }, data });
    }),

  addPurchase: adminProcedure
    .input(
      z.object({
        supplierId: z.string(),
        description: z.string(),
        totalCost: z.number(),
        itemCount: z.number(),
        purchaseDate: z.date(),
        receiptUrl: z.string().optional(),
        notes: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const purchase = await ctx.prisma.supplierPurchase.create({ data: input });
      await ctx.prisma.supplier.update({
        where: { id: input.supplierId },
        data: {
          totalSpent: { increment: input.totalCost },
          totalItems: { increment: input.itemCount },
        },
      });
      return purchase;
    }),
});
