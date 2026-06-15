"use client";

import Link from "next/link";
import Image from "next/image";
import { Heart } from "lucide-react";
import { Card, Badge, Button, formatCurrency } from "@overhaul/ui";

interface ProductCardProps {
  product: {
    id: string;
    slug: string;
    name: string;
    listPrice: number | string;
    compareAtPrice?: number | string | null;
    condition: string;
    images: { url: string; altText?: string | null }[];
    category: { name: string; slug: string };
  };
}

const conditionColors: Record<string, string> = {
  NEW: "success",
  LIKE_NEW: "success",
  EXCELLENT: "info",
  GOOD: "info",
  FAIR: "warning",
  POOR: "warning",
  FOR_PARTS: "destructive",
};

const conditionLabels: Record<string, string> = {
  NEW: "New",
  LIKE_NEW: "Like New",
  EXCELLENT: "Excellent",
  GOOD: "Good",
  FAIR: "Fair",
  POOR: "Poor",
  FOR_PARTS: "For Parts",
};

export function ProductCard({ product }: ProductCardProps) {
  const imageUrl = product.images[0]?.url ?? "https://placehold.co/400x400/f5f5f5/999?text=No+Image";
  const hasDiscount = product.compareAtPrice && Number(product.compareAtPrice) > Number(product.listPrice);

  return (
    <Card className="group overflow-hidden transition-all hover:shadow-md">
      <Link href={`/products/${product.slug}`}>
        <div className="relative aspect-square overflow-hidden bg-muted">
          <Image
            src={imageUrl}
            alt={product.images[0]?.altText ?? product.name}
            fill
            className="object-cover transition-transform group-hover:scale-105"
            sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 25vw"
          />
          <div className="absolute top-2 left-2 flex gap-1">
            <Badge variant={conditionColors[product.condition] as any ?? "secondary"}>
              {conditionLabels[product.condition] ?? product.condition}
            </Badge>
            {hasDiscount && (
              <Badge variant="destructive">Sale</Badge>
            )}
          </div>
          <Button
            variant="ghost"
            size="icon"
            className="absolute top-2 right-2 bg-white/80 hover:bg-white opacity-0 group-hover:opacity-100 transition-opacity"
            onClick={(e) => {
              e.preventDefault();
            }}
          >
            <Heart className="h-4 w-4" />
          </Button>
        </div>
      </Link>
      <div className="p-4">
        <p className="text-xs text-muted-foreground mb-1">{product.category.name}</p>
        <Link href={`/products/${product.slug}`}>
          <h3 className="font-medium text-sm leading-tight line-clamp-2 hover:text-brand-600 transition-colors">
            {product.name}
          </h3>
        </Link>
        <div className="mt-2 flex items-center gap-2">
          <span className="font-bold text-lg">{formatCurrency(Number(product.listPrice))}</span>
          {hasDiscount && (
            <span className="text-sm text-muted-foreground line-through">
              {formatCurrency(Number(product.compareAtPrice))}
            </span>
          )}
        </div>
      </div>
    </Card>
  );
}
