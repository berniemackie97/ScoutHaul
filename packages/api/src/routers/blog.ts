import { z } from "zod";
import { router, publicProcedure, adminProcedure } from "../trpc";

export const blogRouter = router({
  list: publicProcedure
    .input(
      z.object({
        cursor: z.string().optional(),
        limit: z.number().default(10),
        tag: z.string().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = { isPublished: true };
      if (input.tag) where.tags = { has: input.tag };

      const items = await ctx.prisma.blogPost.findMany({
        where: where as any,
        take: input.limit + 1,
        cursor: input.cursor ? { id: input.cursor } : undefined,
        orderBy: { publishedAt: "desc" },
      });

      let nextCursor: string | undefined;
      if (items.length > input.limit) {
        nextCursor = items.pop()?.id;
      }

      return { items, nextCursor };
    }),

  bySlug: publicProcedure
    .input(z.object({ slug: z.string() }))
    .query(async ({ ctx, input }) => {
      return ctx.prisma.blogPost.findUnique({
        where: { slug: input.slug, isPublished: true },
      });
    }),

  create: adminProcedure
    .input(
      z.object({
        title: z.string().min(1),
        content: z.string().min(1),
        excerpt: z.string().optional(),
        coverImage: z.string().optional(),
        tags: z.array(z.string()).default([]),
        isPublished: z.boolean().default(false),
        authorName: z.string(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const slug = input.title
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/(^-|-$)/g, "");

      return ctx.prisma.blogPost.create({
        data: {
          ...input,
          slug: `${slug}-${Date.now().toString(36)}`,
          publishedAt: input.isPublished ? new Date() : null,
        },
      });
    }),

  update: adminProcedure
    .input(
      z.object({
        id: z.string(),
        title: z.string().optional(),
        content: z.string().optional(),
        excerpt: z.string().optional(),
        coverImage: z.string().optional(),
        tags: z.array(z.string()).optional(),
        isPublished: z.boolean().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const { id, ...data } = input;
      const updateData: Record<string, unknown> = { ...data };
      if (input.isPublished) updateData.publishedAt = new Date();
      return ctx.prisma.blogPost.update({ where: { id }, data: updateData });
    }),

  delete: adminProcedure
    .input(z.object({ id: z.string() }))
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.blogPost.delete({ where: { id: input.id } });
    }),

  // Admin list includes drafts
  listAll: adminProcedure.query(async ({ ctx }) => {
    return ctx.prisma.blogPost.findMany({ orderBy: { createdAt: "desc" } });
  }),
});
