"use client";

import Link from "next/link";
import Image from "next/image";
import { Minus, Plus, Trash2, ShoppingBag, ArrowRight } from "lucide-react";
import { Button, Card, Separator, formatCurrency } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function CartPage() {
  const { data: cart, isLoading } = trpc.cart.get.useQuery();
  const utils = trpc.useUtils();

  const updateQuantity = trpc.cart.updateQuantity.useMutation({
    onSuccess: () => utils.cart.get.invalidate(),
  });

  const removeItem = trpc.cart.removeItem.useMutation({
    onSuccess: () => utils.cart.get.invalidate(),
  });

  if (isLoading) {
    return (
      <div className="container mx-auto px-4 py-8">
        <h1 className="text-3xl font-bold mb-8">Shopping Cart</h1>
        <div className="animate-pulse space-y-4">
          {[1, 2].map((i) => (
            <Card key={i} className="p-4">
              <div className="flex gap-4">
                <div className="h-24 w-24 bg-muted rounded" />
                <div className="flex-1 space-y-2">
                  <div className="h-4 bg-muted rounded w-1/3" />
                  <div className="h-5 bg-muted rounded w-1/4" />
                </div>
              </div>
            </Card>
          ))}
        </div>
      </div>
    );
  }

  const items = cart?.items ?? [];
  const subtotal = items.reduce(
    (sum, item) => sum + Number(item.product.listPrice) * item.quantity,
    0
  );
  const shipping = subtotal > 100 ? 0 : 9.99;
  const tax = subtotal * 0.07;
  const total = subtotal + shipping + tax;

  if (items.length === 0) {
    return (
      <div className="container mx-auto px-4 py-16 text-center">
        <ShoppingBag className="h-16 w-16 text-muted-foreground/30 mx-auto mb-4" />
        <h1 className="text-2xl font-bold mb-2">Your cart is empty</h1>
        <p className="text-muted-foreground mb-6">Browse our products and find something you love.</p>
        <Link href="/products">
          <Button>Continue Shopping</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <h1 className="text-3xl font-bold mb-8">Shopping Cart</h1>

      <div className="grid lg:grid-cols-3 gap-8">
        <div className="lg:col-span-2 space-y-4">
          {items.map((item) => (
            <Card key={item.id} className="p-4">
              <div className="flex gap-4">
                <Link href={`/products/${item.product.slug}`}>
                  <div className="relative h-24 w-24 rounded-lg overflow-hidden bg-muted shrink-0">
                    <Image
                      src={item.product.images[0]?.url ?? "https://placehold.co/200x200"}
                      alt={item.product.name}
                      fill
                      className="object-cover"
                    />
                  </div>
                </Link>
                <div className="flex-1 min-w-0">
                  <Link href={`/products/${item.product.slug}`}>
                    <h3 className="font-medium text-sm hover:text-brand-600 transition-colors line-clamp-1">
                      {item.product.name}
                    </h3>
                  </Link>
                  <p className="text-xs text-muted-foreground">{item.product.category.name}</p>
                  <p className="font-bold mt-1">{formatCurrency(Number(item.product.listPrice))}</p>
                  <div className="flex items-center gap-2 mt-2">
                    <Button
                      variant="outline" size="icon" className="h-8 w-8"
                      onClick={() => updateQuantity.mutate({ itemId: item.id, quantity: Math.max(0, item.quantity - 1) })}
                    >
                      <Minus className="h-3 w-3" />
                    </Button>
                    <span className="text-sm font-medium w-8 text-center">{item.quantity}</span>
                    <Button
                      variant="outline" size="icon" className="h-8 w-8"
                      onClick={() => updateQuantity.mutate({ itemId: item.id, quantity: item.quantity + 1 })}
                    >
                      <Plus className="h-3 w-3" />
                    </Button>
                    <Button
                      variant="ghost" size="icon" className="h-8 w-8 text-red-500 ml-auto"
                      onClick={() => removeItem.mutate({ itemId: item.id })}
                    >
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </div>
            </Card>
          ))}
        </div>

        {/* Order summary */}
        <Card className="p-6 h-fit sticky top-24">
          <h2 className="font-bold text-lg mb-4">Order Summary</h2>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Subtotal ({items.length} items)</span>
              <span>{formatCurrency(subtotal)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Shipping</span>
              <span>{shipping === 0 ? "Free" : formatCurrency(shipping)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Tax (est.)</span>
              <span>{formatCurrency(tax)}</span>
            </div>
            <Separator className="my-2" />
            <div className="flex justify-between font-bold text-base">
              <span>Total</span>
              <span>{formatCurrency(total)}</span>
            </div>
          </div>
          {subtotal < 100 && (
            <p className="text-xs text-muted-foreground mt-3">
              Add {formatCurrency(100 - subtotal)} more for free shipping
            </p>
          )}
          <Link href="/checkout">
            <Button size="lg" className="w-full mt-4">
              Checkout <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
          <Link href="/products">
            <Button variant="ghost" className="w-full mt-2">Continue Shopping</Button>
          </Link>
        </Card>
      </div>
    </div>
  );
}
