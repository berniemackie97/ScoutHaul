import { initTRPC, TRPCError } from "@trpc/server";
import superjson from "superjson";
import { type PrismaClient } from "@overhaul/database";

export interface Context {
  prisma: PrismaClient;
  userId: string | null;
  userRole: string | null;
}

const t = initTRPC.context<Context>().create({
  transformer: superjson,
  errorFormatter({ shape }) {
    return shape;
  },
});

export const router = t.router;
export const publicProcedure = t.procedure;
export const createCallerFactory = t.createCallerFactory;

const isAuthed = t.middleware(({ ctx, next }) => {
  if (!ctx.userId) {
    throw new TRPCError({ code: "UNAUTHORIZED" });
  }
  return next({
    ctx: { ...ctx, userId: ctx.userId },
  });
});

const isAdmin = t.middleware(({ ctx, next }) => {
  if (!ctx.userId || !["ADMIN", "MANAGER"].includes(ctx.userRole ?? "")) {
    throw new TRPCError({ code: "FORBIDDEN" });
  }
  return next({
    ctx: { ...ctx, userId: ctx.userId, userRole: ctx.userRole! },
  });
});

const isEmployee = t.middleware(({ ctx, next }) => {
  if (
    !ctx.userId ||
    !["ADMIN", "MANAGER", "EMPLOYEE", "TECHNICIAN"].includes(
      ctx.userRole ?? ""
    )
  ) {
    throw new TRPCError({ code: "FORBIDDEN" });
  }
  return next({
    ctx: { ...ctx, userId: ctx.userId, userRole: ctx.userRole! },
  });
});

export const protectedProcedure = t.procedure.use(isAuthed);
export const adminProcedure = t.procedure.use(isAdmin);
export const employeeProcedure = t.procedure.use(isEmployee);
