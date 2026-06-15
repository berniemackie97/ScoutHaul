import { router } from "./trpc";
import { productsRouter } from "./routers/products";
import { ordersRouter } from "./routers/orders";
import { cartRouter } from "./routers/cart";
import { repairsRouter } from "./routers/repairs";
import { consignmentsRouter } from "./routers/consignments";
import { customersRouter } from "./routers/customers";
import { suppliersRouter } from "./routers/suppliers";
import { analyticsRouter } from "./routers/analytics";
import { blogRouter } from "./routers/blog";
import { newsletterRouter } from "./routers/newsletter";

export const appRouter = router({
  products: productsRouter,
  orders: ordersRouter,
  cart: cartRouter,
  repairs: repairsRouter,
  consignments: consignmentsRouter,
  customers: customersRouter,
  suppliers: suppliersRouter,
  analytics: analyticsRouter,
  blog: blogRouter,
  newsletter: newsletterRouter,
});

export type AppRouter = typeof appRouter;
