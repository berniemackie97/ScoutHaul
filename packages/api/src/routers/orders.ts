import { z } from "zod";
import { TRPCError } from "@trpc/server";
import {
  router,
  protectedProcedure,
  adminProcedure,
  employeeProcedure,
} from "../trpc";

export const ordersRouter = router({
  myOrders: protectedProcedure
    .input(
      z.object({
        cursor: z.string().optional(),
        limit: z.number().min(1).max(50).default(10),
        status: z.string().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = { userId: ctx.userId };
      if (input.status) where.status = input.status;

      const items = await ctx.prisma.order.findMany({
        where: where as any,
        take: input.limit + 1,
        cursor: input.cursor ? { id: input.cursor } : undefined,
        orderBy: { createdAt: "desc" },
        include: {
          items: { include: { product: { include: { images: { take: 1 } } } } },
        },
      });

      let nextCursor: string | undefined;
      if (items.length > input.limit) {
        nextCursor = items.pop()?.id;
      }

      return { items, nextCursor };
    }),

  byId: protectedProcedure
    .input(z.object({ id: z.string() }))
    .query(async ({ ctx, input }) => {
      const order = await ctx.prisma.order.findUnique({
        where: { id: input.id },
        include: {
          items: {
            include: { product: { include: { images: { take: 1 } } } },
          },
          shippingAddress: true,
          user: { select: { email: true, firstName: true, lastName: true } },
        },
      });

      if (!order) throw new TRPCError({ code: "NOT_FOUND" });
      if (
        order.userId !== ctx.userId &&
        !["ADMIN", "MANAGER", "EMPLOYEE"].includes(ctx.userRole ?? "")
      ) {
        throw new TRPCError({ code: "FORBIDDEN" });
      }

      return order;
    }),

  create: protectedProcedure
    .input(
      z.object({
        items: z.array(
          z.object({ productId: z.string(), quantity: z.number().min(1) })
        ),
        shippingAddressId: z.string().optional(),
        fulfillmentMethod: z.enum(["SHIP", "LOCAL_PICKUP", "DELIVERY"]),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const products = await ctx.prisma.product.findMany({
        where: {
          id: { in: input.items.map((i) => i.productId) },
          status: "ACTIVE",
        },
      });

      if (products.length !== input.items.length) {
        throw new TRPCError({
          code: "BAD_REQUEST",
          message: "One or more products are unavailable",
        });
      }

      const orderItems = input.items.map((item) => {
        const product = products.find((p) => p.id === item.productId)!;
        return {
          productId: item.productId,
          quantity: item.quantity,
          unitPrice: product.listPrice,
          totalPrice: product.listPrice.toNumber() * item.quantity,
        };
      });

      const subtotal = orderItems.reduce((sum, i) => sum + i.totalPrice, 0);
      const taxRate = 0.07;
      const taxAmount = subtotal * taxRate;
      const shippingAmount =
        input.fulfillmentMethod === "SHIP" ? (subtotal > 100 ? 0 : 9.99) : 0;
      const totalAmount = subtotal + taxAmount + shippingAmount;

      const orderNumber = `OT-${Date.now().toString(36).toUpperCase()}`;

      const order = await ctx.prisma.order.create({
        data: {
          orderNumber,
          userId: ctx.userId,
          shippingAddressId: input.shippingAddressId,
          channel: "WEBSITE",
          subtotal,
          taxAmount,
          shippingAmount,
          totalAmount,
          fulfillmentMethod: input.fulfillmentMethod as any,
          items: {
            create: orderItems.map((item) => ({
              productId: item.productId,
              quantity: item.quantity,
              unitPrice: item.unitPrice,
              totalPrice: item.totalPrice,
            })),
          },
        },
        include: { items: true },
      });

      await ctx.prisma.product.updateMany({
        where: { id: { in: input.items.map((i) => i.productId) } },
        data: { status: "RESERVED" },
      });

      return order;
    }),

  // Admin routes
  listAll: employeeProcedure
    .input(
      z.object({
        cursor: z.string().optional(),
        limit: z.number().min(1).max(100).default(20),
        status: z.string().optional(),
        channel: z.string().optional(),
        dateFrom: z.date().optional(),
        dateTo: z.date().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = {};
      if (input.status) where.status = input.status;
      if (input.channel) where.channel = input.channel;
      if (input.dateFrom || input.dateTo) {
        where.createdAt = {
          ...(input.dateFrom ? { gte: input.dateFrom } : {}),
          ...(input.dateTo ? { lte: input.dateTo } : {}),
        };
      }

      const items = await ctx.prisma.order.findMany({
        where: where as any,
        take: input.limit + 1,
        cursor: input.cursor ? { id: input.cursor } : undefined,
        orderBy: { createdAt: "desc" },
        include: {
          user: { select: { email: true, firstName: true, lastName: true } },
          items: { include: { product: true } },
        },
      });

      let nextCursor: string | undefined;
      if (items.length > input.limit) {
        nextCursor = items.pop()?.id;
      }

      const totalCount = await ctx.prisma.order.count({ where: where as any });

      return { items, nextCursor, totalCount };
    }),

  updateStatus: employeeProcedure
    .input(
      z.object({
        id: z.string(),
        status: z.string(),
        trackingNumber: z.string().optional(),
        trackingCarrier: z.string().optional(),
        notes: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const data: Record<string, unknown> = { status: input.status };
      if (input.trackingNumber) data.trackingNumber = input.trackingNumber;
      if (input.trackingCarrier) data.trackingCarrier = input.trackingCarrier;
      if (input.notes) data.notes = input.notes;
      if (input.status === "SHIPPED") data.shippedAt = new Date();
      if (input.status === "DELIVERED") data.deliveredAt = new Date();

      if (input.status === "SHIPPED" || input.status === "DELIVERED") {
        const order = await ctx.prisma.order.findUnique({
          where: { id: input.id },
          include: { items: true },
        });
        if (order) {
          await ctx.prisma.product.updateMany({
            where: { id: { in: order.items.map((i) => i.productId) } },
            data: { status: "SOLD", soldAt: new Date() },
          });
        }
      }

      return ctx.prisma.order.update({
        where: { id: input.id },
        data: data as any,
        include: { items: true, user: true },
      });
    }),
});
