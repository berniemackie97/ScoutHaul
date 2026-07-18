import { CartWorkspace } from "@/components/store/cart-workspace";

export const metadata = { title: "Checkout" };

export default function Page() {
  return <CartWorkspace mode="checkout" />;
}
