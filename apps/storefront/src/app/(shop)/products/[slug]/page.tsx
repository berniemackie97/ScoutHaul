"use client";

import { useState } from "react";
import { useParams } from "next/navigation";
import Image from "next/image";
import Link from "next/link";
import {
  Heart, ShoppingCart, Truck, Shield, RotateCcw,
  ChevronLeft, Star, MapPin, Share2,
} from "lucide-react";
import {
  Button, Badge, Card, CardContent, Separator, Tabs, TabsContent, TabsList, TabsTrigger,
  formatCurrency,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const conditionLabels: Record<string, string> = {
  NEW: "New", LIKE_NEW: "Like New", EXCELLENT: "Excellent",
  GOOD: "Good", FAIR: "Fair", POOR: "Poor", FOR_PARTS: "For Parts",
};

export default function ProductDetailPage() {
  const params = useParams();
  const slug = params.slug as string;
  const [selectedImage, setSelectedImage] = useState(0);

  const { data: product, isLoading } = trpc.products.bySlug.useQuery({ slug });

  if (isLoading) {
    return (
      <div className="container mx-auto px-4 py-8">
        <div className="grid md:grid-cols-2 gap-8 animate-pulse">
          <div className="aspect-square bg-muted rounded-xl" />
          <div className="space-y-4">
            <div className="h-4 bg-muted rounded w-1/4" />
            <div className="h-8 bg-muted rounded w-3/4" />
            <div className="h-6 bg-muted rounded w-1/4" />
            <div className="h-20 bg-muted rounded" />
          </div>
        </div>
      </div>
    );
  }

  if (!product) {
    return (
      <div className="container mx-auto px-4 py-16 text-center">
        <h1 className="text-2xl font-bold">Product not found</h1>
        <Link href="/products">
          <Button variant="outline" className="mt-4">
            <ChevronLeft className="h-4 w-4" /> Back to Products
          </Button>
        </Link>
      </div>
    );
  }

  const images = product.images.length > 0
    ? product.images
    : [{ url: "https://placehold.co/600x600/f5f5f5/999?text=No+Image", altText: "No image" }];

  return (
    <div className="container mx-auto px-4 py-8">
      {/* Breadcrumb */}
      <nav className="flex items-center gap-2 text-sm text-muted-foreground mb-6">
        <Link href="/products" className="hover:text-foreground">Products</Link>
        <span>/</span>
        <Link href={`/categories/${product.category.slug}`} className="hover:text-foreground">
          {product.category.name}
        </Link>
        <span>/</span>
        <span className="text-foreground">{product.name}</span>
      </nav>

      <div className="grid md:grid-cols-2 gap-8 lg:gap-12">
        {/* Images */}
        <div className="space-y-4">
          <div className="relative aspect-square overflow-hidden rounded-xl bg-muted">
            <Image
              src={images[selectedImage]?.url ?? ""}
              alt={images[selectedImage]?.altText ?? product.name}
              fill
              className="object-cover"
              priority
            />
          </div>
          {images.length > 1 && (
            <div className="flex gap-2 overflow-x-auto pb-2">
              {images.map((img, i) => (
                <button
                  key={img.url}
                  onClick={() => setSelectedImage(i)}
                  className={`relative h-20 w-20 rounded-lg overflow-hidden shrink-0 border-2 transition-colors ${
                    i === selectedImage ? "border-brand-600" : "border-transparent"
                  }`}
                >
                  <Image src={img.url} alt={img.altText ?? ""} fill className="object-cover" />
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Details */}
        <div>
          <div className="flex items-center gap-2 mb-2">
            <Badge variant="secondary">{product.category.name}</Badge>
            <Badge variant={product.condition === "NEW" || product.condition === "LIKE_NEW" ? "success" : "info"}>
              {conditionLabels[product.condition]}
            </Badge>
          </div>

          <h1 className="text-2xl md:text-3xl font-bold mb-2">{product.name}</h1>

          {product.brand && (
            <p className="text-muted-foreground text-sm mb-4">
              {product.brand} {product.model && `- ${product.model}`} {product.year && `(${product.year})`}
            </p>
          )}

          <div className="flex items-baseline gap-3 mb-6">
            <span className="text-3xl font-bold">{formatCurrency(Number(product.listPrice))}</span>
            {product.compareAtPrice && Number(product.compareAtPrice) > Number(product.listPrice) && (
              <>
                <span className="text-lg text-muted-foreground line-through">
                  {formatCurrency(Number(product.compareAtPrice))}
                </span>
                <Badge variant="destructive">
                  {Math.round((1 - Number(product.listPrice) / Number(product.compareAtPrice)) * 100)}% off
                </Badge>
              </>
            )}
          </div>

          <p className="text-muted-foreground mb-6">{product.shortDescription ?? product.description.slice(0, 200)}</p>

          {/* Actions */}
          <div className="flex gap-3 mb-6">
            <Button size="lg" className="flex-1">
              <ShoppingCart className="h-4 w-4" />
              Add to Cart
            </Button>
            <Button variant="outline" size="lg">
              <Heart className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="lg">
              <Share2 className="h-4 w-4" />
            </Button>
          </div>

          {/* Shipping info */}
          <Card className="mb-6">
            <CardContent className="p-4 space-y-3">
              {product.isShippable && (
                <div className="flex items-center gap-3 text-sm">
                  <Truck className="h-4 w-4 text-brand-600" />
                  <span>Free shipping on orders over $100</span>
                </div>
              )}
              {product.localPickupOnly && (
                <div className="flex items-center gap-3 text-sm">
                  <MapPin className="h-4 w-4 text-brand-600" />
                  <span>Local pickup only - Jacksonville, FL</span>
                </div>
              )}
              <div className="flex items-center gap-3 text-sm">
                <Shield className="h-4 w-4 text-brand-600" />
                <span>Buyer protection on all purchases</span>
              </div>
              <div className="flex items-center gap-3 text-sm">
                <RotateCcw className="h-4 w-4 text-brand-600" />
                <span>7-day return policy</span>
              </div>
            </CardContent>
          </Card>

          {/* SKU + Tags */}
          <div className="text-xs text-muted-foreground space-y-1">
            <p>SKU: {product.sku}</p>
            {product.tags.length > 0 && (
              <div className="flex gap-1 flex-wrap">
                {product.tags.map((tag) => (
                  <Badge key={tag} variant="outline" className="text-xs">{tag}</Badge>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Tabs */}
      <Tabs defaultValue="description" className="mt-12">
        <TabsList>
          <TabsTrigger value="description">Description</TabsTrigger>
          <TabsTrigger value="condition">Condition Details</TabsTrigger>
          <TabsTrigger value="reviews">Reviews ({product._count.reviews})</TabsTrigger>
        </TabsList>
        <TabsContent value="description" className="mt-4">
          <div className="prose max-w-none">
            <p className="text-muted-foreground whitespace-pre-wrap">{product.description}</p>
          </div>
        </TabsContent>
        <TabsContent value="condition" className="mt-4">
          <div className="space-y-4">
            <div>
              <h3 className="font-semibold mb-1">Condition: {conditionLabels[product.condition]}</h3>
              <p className="text-muted-foreground">{product.conditionNotes ?? "No additional condition notes."}</p>
            </div>
            {product.weight && (
              <p className="text-sm text-muted-foreground">Weight: {Number(product.weight)} lbs</p>
            )}
          </div>
        </TabsContent>
        <TabsContent value="reviews" className="mt-4">
          {product.reviews.length === 0 ? (
            <p className="text-muted-foreground">No reviews yet. Be the first to review this product!</p>
          ) : (
            <div className="space-y-4">
              {product.reviews.map((review) => (
                <Card key={review.id} className="p-4">
                  <div className="flex items-center gap-2 mb-2">
                    <div className="flex gap-0.5">
                      {Array.from({ length: review.rating }).map((_, i) => (
                        <Star key={i} className="h-3 w-3 fill-brand-500 text-brand-500" />
                      ))}
                    </div>
                    <span className="text-sm font-medium">
                      {review.user.firstName} {review.user.lastName?.charAt(0)}.
                    </span>
                  </div>
                  {review.title && <p className="font-medium text-sm">{review.title}</p>}
                  {review.content && <p className="text-sm text-muted-foreground mt-1">{review.content}</p>}
                </Card>
              ))}
            </div>
          )}
        </TabsContent>
      </Tabs>
    </div>
  );
}
