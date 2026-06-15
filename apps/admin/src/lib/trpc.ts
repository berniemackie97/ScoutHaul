import { createTRPCReact } from "@trpc/react-query";
import { type AppRouter } from "@overhaul/api";

export const trpc = createTRPCReact<AppRouter>();
