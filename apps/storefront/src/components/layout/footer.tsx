import Link from "next/link";
import { Package, Mail, Phone, MapPin } from "lucide-react";
import { Input, Button, Separator } from "@overhaul/ui";

const footerLinks = {
  shop: [
    { name: "All Products", href: "/products" },
    { name: "Furniture", href: "/categories/furniture" },
    { name: "Electronics", href: "/categories/electronics" },
    { name: "Collectibles", href: "/categories/collectibles" },
    { name: "New Arrivals", href: "/products?sort=newest" },
  ],
  services: [
    { name: "Repair Services", href: "/repairs" },
    { name: "Consignment", href: "/consignment" },
    { name: "Wholesale", href: "/about#wholesale" },
    { name: "Local Pickup", href: "/about#pickup" },
  ],
  company: [
    { name: "About Us", href: "/about" },
    { name: "Blog", href: "/blog" },
    { name: "Contact", href: "/about#contact" },
    { name: "Shipping Policy", href: "/policies/shipping" },
    { name: "Return Policy", href: "/policies/returns" },
    { name: "Privacy Policy", href: "/policies/privacy" },
  ],
};

export function Footer() {
  return (
    <footer className="bg-muted/50 border-t">
      <div className="container mx-auto px-4 py-12">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-8">
          {/* Brand */}
          <div className="lg:col-span-2">
            <Link href="/" className="flex items-center gap-2 mb-4">
              <div className="h-9 w-9 rounded-lg bg-brand-600 flex items-center justify-center">
                <Package className="h-5 w-5 text-white" />
              </div>
              <div>
                <span className="text-lg font-bold">Overhaul</span>
                <span className="text-lg font-light text-muted-foreground ml-1">Trading</span>
              </div>
            </Link>
            <p className="text-sm text-muted-foreground mb-4 max-w-sm">
              Quality secondhand goods at fair prices. We source the best finds
              from estate sales, thrift stores, and liquidation events across Florida.
            </p>
            <div className="space-y-2 text-sm text-muted-foreground">
              <div className="flex items-center gap-2">
                <MapPin className="h-4 w-4" />
                <span>Jacksonville, Florida</span>
              </div>
              <div className="flex items-center gap-2">
                <Mail className="h-4 w-4" />
                <span>hello@overhaultrading.com</span>
              </div>
            </div>

            {/* Newsletter */}
            <div className="mt-6">
              <h4 className="text-sm font-semibold mb-2">Stay in the loop</h4>
              <p className="text-xs text-muted-foreground mb-3">
                Get notified about new inventory and exclusive deals.
              </p>
              <form className="flex gap-2">
                <Input
                  type="email"
                  placeholder="your@email.com"
                  className="max-w-[220px]"
                />
                <Button size="sm">Subscribe</Button>
              </form>
            </div>
          </div>

          {/* Links */}
          <div>
            <h4 className="text-sm font-semibold mb-3">Shop</h4>
            <ul className="space-y-2">
              {footerLinks.shop.map((link) => (
                <li key={link.href}>
                  <Link href={link.href} className="text-sm text-muted-foreground hover:text-foreground transition-colors">
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h4 className="text-sm font-semibold mb-3">Services</h4>
            <ul className="space-y-2">
              {footerLinks.services.map((link) => (
                <li key={link.href}>
                  <Link href={link.href} className="text-sm text-muted-foreground hover:text-foreground transition-colors">
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>

          <div>
            <h4 className="text-sm font-semibold mb-3">Company</h4>
            <ul className="space-y-2">
              {footerLinks.company.map((link) => (
                <li key={link.href}>
                  <Link href={link.href} className="text-sm text-muted-foreground hover:text-foreground transition-colors">
                    {link.name}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <Separator className="my-8" />

        <div className="flex flex-col sm:flex-row justify-between items-center gap-4 text-sm text-muted-foreground">
          <p>&copy; {new Date().getFullYear()} Overhaul Trading Company LLC. All rights reserved.</p>
          <p>Extending the life of quality goods.</p>
        </div>
      </div>
    </footer>
  );
}
