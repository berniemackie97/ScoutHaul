"use client";

import Link from "next/link";
import { ArrowLeft, Heart, Trash2 } from "lucide-react";
import { Button, Card, Badge } from "@overhaul/ui";
import { formatCurrency } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function WishlistPage() {
  const { data: wishlist, isLoading } = trpc.customers.wishlist.useQuery();
  const utils = trpc.useUtils();
  const toggle = trpc.customers.toggleWishlist.useMutation({
    onSuccess: () => utils.customers.wishlist.invalidate(),
  });

  return (
    <div className="container mx-auto px-4 py-8 max-w-4xl">
      <Link href="/account" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> Account
      </Link>

      <h1 className="text-3xl font-bold mb-8">My Wishlist</h1>

      {isLoading ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="h-64 bg-muted rounded-xl animate-pulse" />
          ))}
        </div>
      ) : wishlist?.length === 0 ? (
        <div className="text-center py-16">
          <Heart className="h-12 w-12 mx-auto mb-4 text-muted-foreground" />
          <h2 className="text-xl font-medium mb-2">Your wishlist is empty</h2>
          <p className="text-muted-foreground mb-6">Save items you love for later.</p>
          <Link href="/products" className="text-brand-600 hover:underline">Browse products</Link>
        </div>
      ) : (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {wishlist?.map((item) => (
            <Card key={item.id} className="overflow-hidden group">
              <Link href={`/products/${item.product.slug}`}>
                <div className="aspect-square bg-muted relative">
                  {item.product.images?.[0] && (
                    <img
                      src={item.product.images[0].url}
                      alt={item.product.name}
                      className="w-full h-full object-cover"
                    />
                  )}
                  <Badge className="absolute top-2 left-2" variant="secondary">
                    {item.product.condition.replace("_", " ")}
                  </Badge>
                </div>
              </Link>
              <div className="p-4">
                <Link href={`/products/${item.product.slug}`}>
                  <h3 className="font-medium group-hover:text-brand-600 transition-colors">
                    {item.product.name}
                  </h3>
                </Link>
                <p className="text-lg font-bold text-brand-600 mt-1">
                  {formatCurrency(Number(item.product.listPrice))}
                </p>
                <Button
                  variant="ghost"
                  size="sm"
                  className="mt-2 text-destructive hover:text-destructive"
                  onClick={() => toggle.mutate({ productId: item.product.id })}
                  disabled={toggle.isPending}
                >
                  <Trash2 className="h-4 w-4 mr-1" /> Remove
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
