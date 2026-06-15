import { z } from "zod";
import {
  router,
  publicProcedure,
  protectedProcedure,
  adminProcedure,
} from "../trpc";

export const productsRouter = router({
  list: publicProcedure
    .input(
      z.object({
        cursor: z.string().optional(),
        limit: z.number().min(1).max(50).default(20),
        categorySlug: z.string().optional(),
        condition: z.string().optional(),
        minPrice: z.number().optional(),
        maxPrice: z.number().optional(),
        search: z.string().optional(),
        sortBy: z
          .enum(["createdAt", "listPrice", "name", "viewCount"])
          .default("createdAt"),
        sortOrder: z.enum(["asc", "desc"]).default("desc"),
        isFeatured: z.boolean().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = {
        status: "ACTIVE",
      };

      if (input.categorySlug) {
        where.category = { slug: input.categorySlug };
      }
      if (input.condition) {
        where.condition = input.condition;
      }
      if (input.minPrice || input.maxPrice) {
        where.listPrice = {
          ...(input.minPrice ? { gte: input.minPrice } : {}),
          ...(input.maxPrice ? { lte: input.maxPrice } : {}),
        };
      }
      if (input.search) {
        where.OR = [
          { name: { contains: input.search, mode: "insensitive" } },
          { description: { contains: input.search, mode: "insensitive" } },
          { tags: { hasSome: [input.search.toLowerCase()] } },
        ];
      }
      if (input.isFeatured !== undefined) {
        where.isFeatured = input.isFeatured;
      }

      const items = await ctx.prisma.product.findMany({
        where: where as any,
        take: input.limit + 1,
        cursor: input.cursor ? { id: input.cursor } : undefined,
        orderBy: { [input.sortBy]: input.sortOrder },
        include: {
          images: { orderBy: { sortOrder: "asc" }, take: 1 },
          category: true,
          _count: { select: { reviews: true } },
        },
      });

      let nextCursor: string | undefined;
      if (items.length > input.limit) {
        const nextItem = items.pop();
        nextCursor = nextItem?.id;
      }

      return { items, nextCursor };
    }),

  bySlug: publicProcedure
    .input(z.object({ slug: z.string() }))
    .query(async ({ ctx, input }) => {
      const product = await ctx.prisma.product.findUnique({
        where: { slug: input.slug },
        include: {
          images: { orderBy: { sortOrder: "asc" } },
          category: true,
          reviews: {
            include: { user: { select: { firstName: true, lastName: true } } },
            orderBy: { createdAt: "desc" },
            take: 10,
          },
          _count: { select: { reviews: true, wishlistItems: true } },
        },
      });
      if (!product) return null;

      await ctx.prisma.product.update({
        where: { id: product.id },
        data: { viewCount: { increment: 1 } },
      });

      return product;
    }),

  categories: publicProcedure.query(async ({ ctx }) => {
    return ctx.prisma.category.findMany({
      where: { parentId: null, isActive: true },
      include: {
        children: { where: { isActive: true }, orderBy: { sortOrder: "asc" } },
        _count: { select: { products: { where: { status: "ACTIVE" } } } },
      },
      orderBy: { sortOrder: "asc" },
    });
  }),

  featured: publicProcedure.query(async ({ ctx }) => {
    return ctx.prisma.product.findMany({
      where: { status: "ACTIVE", isFeatured: true },
      include: {
        images: { orderBy: { sortOrder: "asc" }, take: 1 },
        category: true,
      },
      take: 8,
      orderBy: { createdAt: "desc" },
    });
  }),

  create: adminProcedure
    .input(
      z.object({
        name: z.string().min(1),
        description: z.string().min(1),
        shortDescription: z.string().optional(),
        condition: z.string(),
        conditionNotes: z.string().optional(),
        categoryId: z.string(),
        costPrice: z.number().min(0),
        listPrice: z.number().min(0),
        compareAtPrice: z.number().optional(),
        sourceType: z.string(),
        sourceDetails: z.string().optional(),
        weight: z.number().optional(),
        brand: z.string().optional(),
        model: z.string().optional(),
        year: z.number().optional(),
        tags: z.array(z.string()).default([]),
        isFeatured: z.boolean().default(false),
        isShippable: z.boolean().default(true),
        localPickupOnly: z.boolean().default(false),
        quantity: z.number().default(1),
        supplierId: z.string().optional(),
        imageUrls: z.array(z.string()).default([]),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const slug = input.name
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/(^-|-$)/g, "");

      const sku = `OT-${Date.now().toString(36).toUpperCase()}`;

      const product = await ctx.prisma.product.create({
        data: {
          sku,
          name: input.name,
          slug: `${slug}-${sku.slice(-4).toLowerCase()}`,
          description: input.description,
          shortDescription: input.shortDescription,
          condition: input.condition as any,
          conditionNotes: input.conditionNotes,
          categoryId: input.categoryId,
          costPrice: input.costPrice,
          listPrice: input.listPrice,
          compareAtPrice: input.compareAtPrice,
          sourceType: input.sourceType as any,
          sourceDetails: input.sourceDetails,
          weight: input.weight,
          brand: input.brand,
          model: input.model,
          year: input.year,
          tags: input.tags,
          isFeatured: input.isFeatured,
          isShippable: input.isShippable,
          localPickupOnly: input.localPickupOnly,
          quantity: input.quantity,
          supplierId: input.supplierId,
          images: {
            create: input.imageUrls.map((url, i) => ({
              url,
              sortOrder: i,
              isPrimary: i === 0,
            })),
          },
        },
        include: { images: true, category: true },
      });

      return product;
    }),

  update: adminProcedure
    .input(
      z.object({
        id: z.string(),
        name: z.string().optional(),
        description: z.string().optional(),
        condition: z.string().optional(),
        categoryId: z.string().optional(),
        costPrice: z.number().optional(),
        listPrice: z.number().optional(),
        status: z.string().optional(),
        tags: z.array(z.string()).optional(),
        isFeatured: z.boolean().optional(),
        quantity: z.number().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const { id, ...data } = input;
      return ctx.prisma.product.update({
        where: { id },
        data: data as any,
        include: { images: true, category: true },
      });
    }),

  delete: adminProcedure
    .input(z.object({ id: z.string() }))
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.product.update({
        where: { id: input.id },
        data: { status: "ARCHIVED" },
      });
    }),
});
