"use client";

import Link from "next/link";
import { Package, ArrowRight } from "lucide-react";
import { Card, CardContent, Badge } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const categoryIcons: Record<string, string> = {
  furniture: "🪑",
  electronics: "📱",
  collectibles: "🏆",
  household: "🏠",
  "sporting-goods": "⚽",
  "tools-equipment": "🔧",
};

export default function CategoriesPage() {
  const { data: categories, isLoading } = trpc.products.categories.useQuery();

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="mb-8">
        <h1 className="text-3xl font-bold">Shop by Category</h1>
        <p className="text-muted-foreground mt-1">
          Browse our curated collection organized by category
        </p>
      </div>

      {isLoading ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-48 bg-muted rounded-xl animate-pulse" />
          ))}
        </div>
      ) : (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-6">
          {categories?.map((cat) => (
            <Link key={cat.id} href={`/categories/${cat.slug}`}>
              <Card className="group hover:shadow-lg transition-all hover:border-brand-300 h-full">
                <CardContent className="p-6 flex flex-col h-full">
                  <div className="text-4xl mb-4">{categoryIcons[cat.slug] ?? "📦"}</div>
                  <h2 className="text-xl font-bold mb-2 group-hover:text-brand-600 transition-colors">
                    {cat.name}
                  </h2>
                  {cat.description && (
                    <p className="text-sm text-muted-foreground mb-4 flex-1">{cat.description}</p>
                  )}
                  <div className="flex items-center justify-between mt-auto pt-4">
                    <Badge variant="secondary">
                      {cat._count.products} {cat._count.products === 1 ? "item" : "items"}
                    </Badge>
                    <ArrowRight className="h-4 w-4 text-muted-foreground group-hover:text-brand-600 transition-colors" />
                  </div>
                  {cat.children.length > 0 && (
                    <div className="flex flex-wrap gap-1.5 mt-3 pt-3 border-t">
                      {cat.children.map((child) => (
                        <Badge key={child.id} variant="outline" className="text-xs">
                          {child.name}
                        </Badge>
                      ))}
                    </div>
                  )}
                </CardContent>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
