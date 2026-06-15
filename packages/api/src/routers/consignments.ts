import { z } from "zod";
import { router, protectedProcedure, employeeProcedure } from "../trpc";

export const consignmentsRouter = router({
  submit: protectedProcedure
    .input(
      z.object({
        itemDescription: z.string().min(1),
        category: z.string().optional(),
        condition: z.string(),
        estimatedValue: z.number().optional(),
        imageUrls: z.array(z.string()).default([]),
        notes: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const submissionNumber = `CS-${Date.now().toString(36).toUpperCase()}`;
      return ctx.prisma.consignmentSubmission.create({
        data: {
          submissionNumber,
          userId: ctx.userId,
          itemDescription: input.itemDescription,
          category: input.category,
          condition: input.condition as any,
          estimatedValue: input.estimatedValue,
          imageUrls: input.imageUrls,
          notes: input.notes,
        },
      });
    }),

  mySubmissions: protectedProcedure.query(async ({ ctx }) => {
    return ctx.prisma.consignmentSubmission.findMany({
      where: { userId: ctx.userId },
      orderBy: { createdAt: "desc" },
    });
  }),

  listAll: employeeProcedure
    .input(z.object({ status: z.string().optional() }))
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = {};
      if (input.status) where.status = input.status;

      return ctx.prisma.consignmentSubmission.findMany({
        where: where as any,
        orderBy: { createdAt: "desc" },
        include: {
          user: { select: { email: true, firstName: true, lastName: true } },
        },
      });
    }),

  updateStatus: employeeProcedure
    .input(
      z.object({
        id: z.string(),
        status: z.string(),
        agreedPrice: z.number().optional(),
        commissionRate: z.number().optional(),
        notes: z.string().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const { id, ...data } = input;
      const updateData: Record<string, unknown> = { ...data };

      if (input.status === "LISTED") updateData.listedAt = new Date();
      if (input.status === "SOLD") updateData.soldAt = new Date();
      if (input.status === "PAID_OUT") updateData.paidOutAt = new Date();

      return ctx.prisma.consignmentSubmission.update({
        where: { id },
        data: updateData as any,
      });
    }),
});
