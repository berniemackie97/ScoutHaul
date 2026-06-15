import { z } from "zod";
import { router, protectedProcedure, employeeProcedure } from "../trpc";

export const repairsRouter = router({
  submit: protectedProcedure
    .input(
      z.object({
        itemDescription: z.string().min(1),
        itemBrand: z.string().optional(),
        itemModel: z.string().optional(),
        issueDescription: z.string().min(1),
        priority: z.enum(["LOW", "NORMAL", "HIGH", "RUSH"]).default("NORMAL"),
        imageUrls: z.array(z.string()).default([]),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const jobNumber = `RJ-${Date.now().toString(36).toUpperCase()}`;
      return ctx.prisma.repairJob.create({
        data: {
          jobNumber,
          userId: ctx.userId,
          ...input,
          priority: input.priority as any,
        },
      });
    }),

  myJobs: protectedProcedure.query(async ({ ctx }) => {
    return ctx.prisma.repairJob.findMany({
      where: { userId: ctx.userId },
      orderBy: { createdAt: "desc" },
    });
  }),

  byId: protectedProcedure
    .input(z.object({ id: z.string() }))
    .query(async ({ ctx, input }) => {
      return ctx.prisma.repairJob.findFirst({
        where: { id: input.id, userId: ctx.userId },
      });
    }),

  approveQuote: protectedProcedure
    .input(z.object({ id: z.string() }))
    .mutation(async ({ ctx, input }) => {
      return ctx.prisma.repairJob.update({
        where: { id: input.id, userId: ctx.userId },
        data: { status: "APPROVED", approvedAt: new Date() },
      });
    }),

  // Employee/Admin routes
  listAll: employeeProcedure
    .input(
      z.object({
        status: z.string().optional(),
        priority: z.string().optional(),
      })
    )
    .query(async ({ ctx, input }) => {
      const where: Record<string, unknown> = {};
      if (input.status) where.status = input.status;
      if (input.priority) where.priority = input.priority;

      return ctx.prisma.repairJob.findMany({
        where: where as any,
        orderBy: { createdAt: "desc" },
        include: {
          user: { select: { email: true, firstName: true, lastName: true, phone: true } },
        },
      });
    }),

  updateJob: employeeProcedure
    .input(
      z.object({
        id: z.string(),
        status: z.string().optional(),
        diagnosis: z.string().optional(),
        estimatedCost: z.number().optional(),
        finalCost: z.number().optional(),
        technicianNotes: z.string().optional(),
        partsUsed: z.string().optional(),
        laborHours: z.number().optional(),
      })
    )
    .mutation(async ({ ctx, input }) => {
      const { id, ...data } = input;
      const updateData: Record<string, unknown> = { ...data };

      if (input.status === "DIAGNOSING") updateData.diagnosedAt = new Date();
      if (input.status === "COMPLETED") updateData.completedAt = new Date();
      if (input.status === "PICKED_UP") updateData.pickedUpAt = new Date();
      if (input.status === "RECEIVED") updateData.receivedAt = new Date();

      return ctx.prisma.repairJob.update({
        where: { id },
        data: updateData as any,
        include: { user: true },
      });
    }),
});
