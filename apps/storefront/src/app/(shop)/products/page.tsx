"use client";

import { useState } from "react";
import { Search, SlidersHorizontal, Grid3X3, LayoutList } from "lucide-react";
import {
  Button, Input, Card, Badge,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  Separator,
} from "@overhaul/ui";
import { ProductCard } from "@/components/products/product-card";
import { trpc } from "@/lib/trpc";

const conditions = [
  { value: "all", label: "All Conditions" },
  { value: "NEW", label: "New" },
  { value: "LIKE_NEW", label: "Like New" },
  { value: "EXCELLENT", label: "Excellent" },
  { value: "GOOD", label: "Good" },
  { value: "FAIR", label: "Fair" },
];

const sortOptions = [
  { value: "createdAt-desc", label: "Newest First" },
  { value: "createdAt-asc", label: "Oldest First" },
  { value: "listPrice-asc", label: "Price: Low to High" },
  { value: "listPrice-desc", label: "Price: High to Low" },
  { value: "name-asc", label: "Name: A-Z" },
  { value: "viewCount-desc", label: "Most Popular" },
];

export default function ProductsPage() {
  const [search, setSearch] = useState("");
  const [condition, setCondition] = useState("all");
  const [sort, setSort] = useState("createdAt-desc");
  const [view, setView] = useState<"grid" | "list">("grid");

  const [sortBy, sortOrder] = sort.split("-") as [string, "asc" | "desc"];

  const { data, isLoading } = trpc.products.list.useQuery({
    search: search || undefined,
    condition: condition !== "all" ? condition : undefined,
    sortBy: sortBy as any,
    sortOrder,
    limit: 20,
  });

  return (
    <div className="container mx-auto px-4 py-8">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-3xl font-bold">Shop All Products</h1>
        <p className="text-muted-foreground mt-1">
          Browse our curated collection of quality secondhand goods
        </p>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3 mb-6">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search products..."
            className="pl-10"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Select value={condition} onValueChange={setCondition}>
          <SelectTrigger className="w-[180px]">
            <SelectValue placeholder="All Conditions" />
          </SelectTrigger>
          <SelectContent>
            {conditions.map((c) => (
              <SelectItem key={c.value} value={c.value}>{c.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={sort} onValueChange={setSort}>
          <SelectTrigger className="w-[200px]">
            <SelectValue placeholder="Sort by" />
          </SelectTrigger>
          <SelectContent>
            {sortOptions.map((s) => (
              <SelectItem key={s.value} value={s.value}>{s.label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex gap-1">
          <Button variant={view === "grid" ? "default" : "ghost"} size="icon" onClick={() => setView("grid")}>
            <Grid3X3 className="h-4 w-4" />
          </Button>
          <Button variant={view === "list" ? "default" : "ghost"} size="icon" onClick={() => setView("list")}>
            <LayoutList className="h-4 w-4" />
          </Button>
        </div>
      </div>

      <Separator className="mb-6" />

      {/* Results */}
      {isLoading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
          {Array.from({ length: 8 }).map((_, i) => (
            <Card key={i} className="overflow-hidden animate-pulse">
              <div className="aspect-square bg-muted" />
              <div className="p-4 space-y-2">
                <div className="h-3 bg-muted rounded w-1/3" />
                <div className="h-4 bg-muted rounded w-2/3" />
                <div className="h-5 bg-muted rounded w-1/4" />
              </div>
            </Card>
          ))}
        </div>
      ) : data?.items.length === 0 ? (
        <div className="text-center py-16">
          <p className="text-lg text-muted-foreground">No products found</p>
          <p className="text-sm text-muted-foreground mt-1">Try adjusting your search or filters</p>
        </div>
      ) : (
        <div className={view === "grid"
          ? "grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6"
          : "space-y-4"
        }>
          {data?.items.map((product) => (
            <ProductCard key={product.id} product={product as any} />
          ))}
        </div>
      )}

      {/* Load more */}
      {data?.nextCursor && (
        <div className="text-center mt-8">
          <Button variant="outline">Load More Products</Button>
        </div>
      )}
    </div>
  );
}
