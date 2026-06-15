import Link from "next/link";
import {
  Package,
  Truck,
  Shield,
  Recycle,
  ArrowRight,
  Wrench,
  HandCoins,
  Star,
} from "lucide-react";
import { Button, Card, CardContent, Badge } from "@overhaul/ui";

const categories = [
  { name: "Furniture", slug: "furniture", icon: "🪑", count: 45 },
  { name: "Electronics", slug: "electronics", icon: "📱", count: 32 },
  { name: "Collectibles", slug: "collectibles", icon: "🏆", count: 28 },
  { name: "Household", slug: "household", icon: "🏠", count: 56 },
  { name: "Sporting Goods", slug: "sporting-goods", icon: "⚾", count: 19 },
  { name: "Tools & Equipment", slug: "tools-equipment", icon: "🔧", count: 23 },
];

const valueProps = [
  {
    icon: Shield,
    title: "Quality Guaranteed",
    description: "Every item inspected and honestly described. What you see is what you get.",
  },
  {
    icon: Truck,
    title: "Free Shipping 100+",
    description: "Free shipping on orders over $100. Local pickup always available in Jacksonville.",
  },
  {
    icon: Recycle,
    title: "Sustainable Shopping",
    description: "Extend the life of quality goods. Better for your wallet, better for the planet.",
  },
  {
    icon: HandCoins,
    title: "Fair Prices Always",
    description: "Sourced smart, priced fair. 50-60% below retail on quality secondhand items.",
  },
];

export default function HomePage() {
  return (
    <div>
      {/* Hero */}
      <section className="relative bg-gradient-to-br from-brand-50 via-white to-brand-50 dark:from-brand-950 dark:via-background dark:to-brand-950">
        <div className="container mx-auto px-4 py-16 md:py-24">
          <div className="max-w-3xl">
            <Badge variant="secondary" className="mb-4">
              Florida&apos;s Trusted Secondhand Marketplace
            </Badge>
            <h1 className="text-4xl md:text-6xl font-bold tracking-tight mb-6">
              Quality finds.
              <br />
              <span className="text-brand-600">Fair prices.</span>
              <br />
              Second life.
            </h1>
            <p className="text-lg text-muted-foreground mb-8 max-w-xl">
              Curated secondhand furniture, electronics, and collectibles sourced
              from estate sales and liquidation events across Florida. Every item
              inspected, honestly described, and priced to sell.
            </p>
            <div className="flex flex-wrap gap-3">
              <Link href="/products">
                <Button size="xl">
                  Shop All Products
                  <ArrowRight className="h-4 w-4" />
                </Button>
              </Link>
              <Link href="/repairs">
                <Button variant="outline" size="xl">
                  <Wrench className="h-4 w-4" />
                  Repair Services
                </Button>
              </Link>
            </div>
          </div>
        </div>
      </section>

      {/* Value Props */}
      <section className="border-y bg-muted/30">
        <div className="container mx-auto px-4 py-8">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
            {valueProps.map((prop) => (
              <div key={prop.title} className="flex items-start gap-3">
                <div className="h-10 w-10 rounded-lg bg-brand-100 dark:bg-brand-900 flex items-center justify-center shrink-0">
                  <prop.icon className="h-5 w-5 text-brand-600" />
                </div>
                <div>
                  <h3 className="font-semibold text-sm">{prop.title}</h3>
                  <p className="text-xs text-muted-foreground mt-0.5">{prop.description}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Categories */}
      <section className="container mx-auto px-4 py-16">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h2 className="text-2xl font-bold">Shop by Category</h2>
            <p className="text-muted-foreground mt-1">
              Browse our curated collection of secondhand goods
            </p>
          </div>
          <Link href="/categories">
            <Button variant="ghost">
              View All <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
        </div>
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
          {categories.map((cat) => (
            <Link key={cat.slug} href={`/categories/${cat.slug}`}>
              <Card className="text-center p-6 hover:shadow-md transition-all hover:border-brand-300 cursor-pointer group">
                <div className="text-4xl mb-3">{cat.icon}</div>
                <h3 className="font-semibold text-sm group-hover:text-brand-600 transition-colors">
                  {cat.name}
                </h3>
                <p className="text-xs text-muted-foreground mt-1">
                  {cat.count} items
                </p>
              </Card>
            </Link>
          ))}
        </div>
      </section>

      {/* Featured Products placeholder */}
      <section className="bg-muted/30 py-16">
        <div className="container mx-auto px-4">
          <div className="flex items-center justify-between mb-8">
            <div>
              <h2 className="text-2xl font-bold">Featured Finds</h2>
              <p className="text-muted-foreground mt-1">
                Hand-picked items with the best value
              </p>
            </div>
            <Link href="/products?featured=true">
              <Button variant="ghost">
                View All <ArrowRight className="h-4 w-4" />
              </Button>
            </Link>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
            {[1, 2, 3, 4].map((i) => (
              <Card key={i} className="overflow-hidden">
                <div className="aspect-square bg-muted flex items-center justify-center">
                  <Package className="h-12 w-12 text-muted-foreground/30" />
                </div>
                <CardContent className="p-4">
                  <p className="text-xs text-muted-foreground">Featured Item</p>
                  <p className="font-medium text-sm mt-1">Products will appear here</p>
                  <p className="font-bold mt-2">$--.-</p>
                </CardContent>
              </Card>
            ))}
          </div>
        </div>
      </section>

      {/* Services CTA */}
      <section className="container mx-auto px-4 py-16">
        <div className="grid md:grid-cols-2 gap-6">
          <Card className="p-8 bg-gradient-to-br from-brand-50 to-white dark:from-brand-950 dark:to-background border-brand-200 dark:border-brand-800">
            <Wrench className="h-10 w-10 text-brand-600 mb-4" />
            <h3 className="text-xl font-bold mb-2">Repair Services</h3>
            <p className="text-muted-foreground mb-4">
              Got something broken? We fix electronics, furniture, and appliances
              at fair rates. Get a free quote today.
            </p>
            <Link href="/repairs">
              <Button>
                Get a Repair Quote <ArrowRight className="h-4 w-4" />
              </Button>
            </Link>
          </Card>

          <Card className="p-8 bg-gradient-to-br from-green-50 to-white dark:from-green-950 dark:to-background border-green-200 dark:border-green-800">
            <HandCoins className="h-10 w-10 text-green-600 mb-4" />
            <h3 className="text-xl font-bold mb-2">Sell on Consignment</h3>
            <p className="text-muted-foreground mb-4">
              Have quality items to sell? We handle the listing, selling, and
              shipping. You get paid when it sells.
            </p>
            <Link href="/consignment">
              <Button variant="outline" className="border-green-600 text-green-700 hover:bg-green-50">
                Start Consigning <ArrowRight className="h-4 w-4" />
              </Button>
            </Link>
          </Card>
        </div>
      </section>

      {/* Testimonials */}
      <section className="bg-muted/30 py-16">
        <div className="container mx-auto px-4 text-center">
          <h2 className="text-2xl font-bold mb-8">What Our Customers Say</h2>
          <div className="grid md:grid-cols-3 gap-6 max-w-4xl mx-auto">
            {[
              { name: "Sarah M.", text: "Found an amazing mid-century desk for half the price of retail. Excellent condition, exactly as described.", rating: 5 },
              { name: "James T.", text: "Had my laptop repaired here. Quick turnaround, fair price, and it works perfectly now. Highly recommend.", rating: 5 },
              { name: "Maria L.", text: "Consigned some furniture and it sold within a week. Easy process and I got a great payout.", rating: 5 },
            ].map((review) => (
              <Card key={review.name} className="p-6 text-left">
                <div className="flex gap-0.5 mb-3">
                  {Array.from({ length: review.rating }).map((_, i) => (
                    <Star key={i} className="h-4 w-4 fill-brand-500 text-brand-500" />
                  ))}
                </div>
                <p className="text-sm text-muted-foreground mb-4">&ldquo;{review.text}&rdquo;</p>
                <p className="text-sm font-semibold">{review.name}</p>
              </Card>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
