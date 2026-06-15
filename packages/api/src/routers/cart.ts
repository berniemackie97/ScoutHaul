import { z } from "zod";
import { router, protectedProcedure } from "../trpc";

export const cartRouter = router({
  get: protectedProcedure.query(async ({ ctx }) => {
    let cart = await ctx.prisma.cart.findUnique({
      where: { userId: ctx.userId },
      include: {
        items: {
          include: {
            product: {
              include: {
                images: { orderBy: { sortOrder: "asc" }, take: 1 },
                category: true,
              },
            },
          },
          orderBy: { createdAt: "asc" },
        },
      },
    });

    if (!cart) {
      cart = await ctx.prisma.cart.create({
        data: { userId: ctx.userId },
        include: {
          items: {
            include: {
              product: { include: { images: { take: 1 }, category: true } },
            },
          },
        },
      });
    }

    return cart;
  }),

  addItem: protectedProcedure
    .input(z.object({ productId: z.string(), quantity: z.number().min(1).default(1) }))
    .mutation(async ({ ctx, input }) => {
      let cart = await ctx.prisma.cart.findUnique({
        where: { userId: ctx.userId },
      });

      if (!cart) {
        cart = await ctx.prisma.cart.create({ data: { userId: ctx.userId } });
      }

      const existing = await ctx.prisma.cartItem.findUnique({
        where: { cartId_productId: { cartId: cart.id, productId: input.productId } },
      });

      if (existing) {
        return ctx.prisma.cartItem.update({
          where: { id: existing.id },
          data: { quantity: existing.quantity + input.quantity },
        });
      }

      return ctx.prisma.cartItem.create({
        data: {
          cartId: cart.id,
          productId: input.productId,
          quantity: input.quantity,
        },
      });
    }),

  updateQuantity: protectedProcedure
    .input(z.object({ itemId: z.string(), quantity: z.number().min(0) }))
    .mutation(async ({ ctx, input }) => {
      if (input.quantity === 0) {
        return ctx.prisma.cartItem.delete({ where: { id: input.itemId } });
      }
      return ctx.prisma.cartItem.update({
        where: { id: input.itemId },
        data: { quantity: input.quantity },
      });
    }),

  removeItem: protectedProcedure
    .input(z.object({ itemId: z.string() }))
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.cartItem.delete({ where: { id: input.itemId } });
    }),

  clear: protectedProcedure.mutation(async ({ ctx }) => {
    const cart = await ctx.prisma.cart.findUnique({
      where: { userId: ctx.userId },
    });
    if (!cart) return;
    return ctx.prisma.cartItem.deleteMany({ where: { cartId: cart.id } });
  }),
});
