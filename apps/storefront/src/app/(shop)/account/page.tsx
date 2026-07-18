"use client";

import Link from "next/link";
import { ShoppingBag, Heart, Wrench, Package, HandCoins } from "lucide-react";
import { Card } from "@overhaul/ui";

const accountLinks = [
  { name: "My Offers", href: "/offers", icon: HandCoins, description: "Track accepted, declined, and countered offers" },
  { name: "My Orders", href: "/account/orders", icon: ShoppingBag, description: "View order history and track shipments" },
  { name: "Wishlist", href: "/account/wishlist", icon: Heart, description: "Items you've saved for later" },
  { name: "Repair Jobs", href: "/repairs", icon: Wrench, description: "Track your repair requests" },
  { name: "Consignments", href: "/consignment", icon: Package, description: "Your consignment submissions" },
];

export default function AccountPage() {
  return (
    <div className="container mx-auto px-4 py-8 max-w-3xl">
      <h1 className="text-3xl font-bold mb-8">My Account</h1>
      <div className="grid sm:grid-cols-2 gap-4">
        {accountLinks.map((link) => (
          <Link key={link.href} href={link.href}>
            <Card className="p-6 hover:shadow-md transition-shadow cursor-pointer group h-full">
              <link.icon className="h-8 w-8 text-brand-600 mb-3" />
              <h3 className="font-semibold group-hover:text-brand-600 transition-colors">{link.name}</h3>
              <p className="text-sm text-muted-foreground mt-1">{link.description}</p>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}
