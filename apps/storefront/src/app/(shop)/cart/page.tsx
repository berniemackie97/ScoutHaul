import { CartWorkspace } from "@/components/store/cart-workspace";

export const metadata = { title: "Cart" };

export default function Page() {
  return <CartWorkspace mode="cart" />;
}
