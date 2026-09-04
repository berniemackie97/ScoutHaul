import { CartWorkspace } from "@/components/store/cart-workspace";

export const metadata = { title: "Checkout",
  robots: { index: false, follow: true },
};
export const dynamic = "force-dynamic";
export const revalidate = 0;

export default function Page() {
  return <CartWorkspace mode="checkout" />;
}
