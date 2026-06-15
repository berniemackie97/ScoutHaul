"use client";

import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Badge } from "@overhaul/ui";
import { ProductCard } from "@/components/products/product-card";
import { trpc } from "@/lib/trpc";

export default function CategoryPage() {
  const { slug } = useParams<{ slug: string }>();
  const { data: categories } = trpc.products.categories.useQuery();
  const category = categories?.find((c) => c.slug === slug);

  const { data, isLoading } = trpc.products.list.useQuery({
    category: category?.id,
    limit: 40,
  }, { enabled: !!category?.id });

  return (
    <div className="container mx-auto px-4 py-8">
      <Link href="/categories" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> All Categories
      </Link>

      <div className="mb-8">
        <h1 className="text-3xl font-bold">{category?.name ?? slug}</h1>
        {category?.description && (
          <p className="text-muted-foreground mt-1">{category.description}</p>
        )}
        {category?.children && category.children.length > 0 && (
          <div className="flex flex-wrap gap-2 mt-4">
            {category.children.map((child) => (
              <Badge key={child.id} variant="outline">{child.name}</Badge>
            ))}
          </div>
        )}
      </div>

      {isLoading ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-80 bg-muted rounded-xl animate-pulse" />
          ))}
        </div>
      ) : data?.items.length === 0 ? (
        <div className="text-center py-16">
          <p className="text-xl font-medium mb-2">No products in this category yet</p>
          <p className="text-muted-foreground">Check back soon for new arrivals.</p>
        </div>
      ) : (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {data?.items.map((product) => (
            <ProductCard key={product.id} product={product as any} />
          ))}
        </div>
      )}
    </div>
  );
}
