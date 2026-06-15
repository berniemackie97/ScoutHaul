import { z } from "zod";
import { router, adminProcedure } from "../trpc";

export const analyticsRouter = router({
  dashboard: adminProcedure.query(async ({ ctx }) => {
    const now = new Date();
    const thirtyDaysAgo = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000);
    const sevenDaysAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);

    const [
      totalRevenue,
      monthlyOrders,
      weeklyOrders,
      activeProducts,
      totalCustomers,
      pendingRepairs,
      recentOrders,
      topProducts,
      channelBreakdown,
    ] = await Promise.all([
      ctx.prisma.order.aggregate({
        where: { createdAt: { gte: thirtyDaysAgo }, status: { notIn: ["CANCELLED", "REFUNDED"] } },
        _sum: { totalAmount: true },
      }),
      ctx.prisma.order.count({
        where: { createdAt: { gte: thirtyDaysAgo }, status: { notIn: ["CANCELLED", "REFUNDED"] } },
      }),
      ctx.prisma.order.count({
        where: { createdAt: { gte: sevenDaysAgo }, status: { notIn: ["CANCELLED", "REFUNDED"] } },
      }),
      ctx.prisma.product.count({ where: { status: "ACTIVE" } }),
      ctx.prisma.user.count({ where: { role: "CUSTOMER" } }),
      ctx.prisma.repairJob.count({
        where: { status: { in: ["SUBMITTED", "RECEIVED", "DIAGNOSING", "IN_PROGRESS"] } },
      }),
      ctx.prisma.order.findMany({
        take: 5,
        orderBy: { createdAt: "desc" },
        include: {
          user: { select: { firstName: true, lastName: true, email: true } },
          items: { include: { product: true } },
        },
      }),
      ctx.prisma.orderItem.groupBy({
        by: ["productId"],
        _sum: { quantity: true, totalPrice: true },
        orderBy: { _sum: { totalPrice: "desc" } },
        take: 5,
      }),
      ctx.prisma.order.groupBy({
        by: ["channel"],
        where: { createdAt: { gte: thirtyDaysAgo }, status: { notIn: ["CANCELLED", "REFUNDED"] } },
        _sum: { totalAmount: true },
        _count: true,
      }),
    ]);

    const productDetails = await ctx.prisma.product.findMany({
      where: { id: { in: topProducts.map((p) => p.productId) } },
      include: { images: { take: 1 } },
    });

    return {
      kpis: {
        totalRevenue30d: totalRevenue._sum.totalAmount ?? 0,
        orders30d: monthlyOrders,
        orders7d: weeklyOrders,
        activeProducts,
        totalCustomers,
        pendingRepairs,
        avgOrderValue:
          monthlyOrders > 0
            ? Number(totalRevenue._sum.totalAmount ?? 0) / monthlyOrders
            : 0,
      },
      recentOrders,
      topProducts: topProducts.map((tp) => ({
        ...tp,
        product: productDetails.find((p) => p.id === tp.productId),
      })),
      channelBreakdown,
    };
  }),

  revenue: adminProcedure
    .input(
      z.object({
        period: z.enum(["7d", "30d", "90d", "1y"]).default("30d"),
      })
    )
    .query(async ({ ctx, input }) => {
      const daysMap = { "7d": 7, "30d": 30, "90d": 90, "1y": 365 };
      const days = daysMap[input.period];
      const since = new Date(Date.now() - days * 24 * 60 * 60 * 1000);

      return ctx.prisma.dailyMetrics.findMany({
        where: { date: { gte: since } },
        orderBy: { date: "asc" },
      });
    }),

  inventoryValue: adminProcedure.query(async ({ ctx }) => {
    const result = await ctx.prisma.product.aggregate({
      where: { status: { in: ["ACTIVE", "DRAFT", "RESERVED"] } },
      _sum: { costPrice: true, listPrice: true },
      _count: true,
    });

    const byCategory = await ctx.prisma.product.groupBy({
      by: ["categoryId"],
      where: { status: { in: ["ACTIVE", "DRAFT", "RESERVED"] } },
      _sum: { costPrice: true, listPrice: true },
      _count: true,
    });

    const categories = await ctx.prisma.category.findMany({
      where: { id: { in: byCategory.map((b) => b.categoryId) } },
    });

    return {
      totalCost: result._sum.costPrice ?? 0,
      totalRetail: result._sum.listPrice ?? 0,
      totalItems: result._count,
      potentialProfit:
        Number(result._sum.listPrice ?? 0) - Number(result._sum.costPrice ?? 0),
      byCategory: byCategory.map((b) => ({
        ...b,
        category: categories.find((c) => c.id === b.categoryId),
      })),
    };
  }),

  margins: adminProcedure.query(async ({ ctx }) => {
    const soldItems = await ctx.prisma.product.findMany({
      where: { status: "SOLD" },
      select: { costPrice: true, listPrice: true, sourceType: true, categoryId: true },
    });

    const totalCost = soldItems.reduce((sum, i) => sum + Number(i.costPrice), 0);
    const totalRevenue = soldItems.reduce((sum, i) => sum + Number(i.listPrice), 0);

    const bySource = soldItems.reduce(
      (acc, item) => {
        const key = item.sourceType;
        if (!acc[key]) acc[key] = { cost: 0, revenue: 0, count: 0 };
        acc[key]!.cost += Number(item.costPrice);
        acc[key]!.revenue += Number(item.listPrice);
        acc[key]!.count += 1;
        return acc;
      },
      {} as Record<string, { cost: number; revenue: number; count: number }>
    );

    return {
      overallMargin: totalRevenue > 0 ? ((totalRevenue - totalCost) / totalRevenue) * 100 : 0,
      totalCost,
      totalRevenue,
      grossProfit: totalRevenue - totalCost,
      itemsSold: soldItems.length,
      bySource,
    };
  }),
});
