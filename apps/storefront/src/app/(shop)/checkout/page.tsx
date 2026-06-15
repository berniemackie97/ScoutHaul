"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft, CreditCard, Truck, Shield, Lock } from "lucide-react";
import {
  Button, Input, Label, Card, CardContent, CardHeader, CardTitle,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  Separator,
} from "@overhaul/ui";
import { formatCurrency } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const US_STATES = [
  "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA",
  "KS","KY","LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ",
  "NM","NY","NC","ND","OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT",
  "VA","WA","WV","WI","WY",
];

export default function CheckoutPage() {
  const { data: cart } = trpc.cart.get.useQuery();
  const [step, setStep] = useState<"shipping" | "payment" | "review">("shipping");
  const [shipping, setShipping] = useState({
    firstName: "", lastName: "", email: "", phone: "",
    street: "", apt: "", city: "", state: "FL", zip: "",
  });

  const items = cart?.items ?? [];
  const subtotal = items.reduce((sum, item) => sum + Number(item.product.listPrice) * item.quantity, 0);
  const shippingCost = subtotal >= 100 ? 0 : 9.99;
  const tax = subtotal * 0.07;
  const total = subtotal + shippingCost + tax;

  if (items.length === 0) {
    return (
      <div className="container mx-auto px-4 py-16 text-center">
        <h1 className="text-2xl font-bold mb-4">Your cart is empty</h1>
        <p className="text-muted-foreground mb-6">Add items to your cart before checking out.</p>
        <Link href="/products">
          <Button>Continue Shopping</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <Link href="/cart" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mb-6">
        <ArrowLeft className="h-4 w-4" /> Back to cart
      </Link>

      <h1 className="text-3xl font-bold mb-8">Checkout</h1>

      {/* Steps indicator */}
      <div className="flex items-center gap-2 mb-8">
        {["Shipping", "Payment", "Review"].map((s, i) => {
          const stepKey = s.toLowerCase() as typeof step;
          const isActive = step === stepKey;
          const isPast = (step === "payment" && i === 0) || (step === "review" && i < 2);
          return (
            <div key={s} className="flex items-center gap-2">
              {i > 0 && <div className={`h-px w-8 ${isPast ? "bg-brand-600" : "bg-border"}`} />}
              <div className={`flex items-center gap-2 px-3 py-1.5 rounded-full text-sm font-medium ${
                isActive ? "bg-brand-600 text-white" : isPast ? "bg-brand-100 text-brand-700" : "bg-muted text-muted-foreground"
              }`}>
                <span>{i + 1}</span>
                <span className="hidden sm:inline">{s}</span>
              </div>
            </div>
          );
        })}
      </div>

      <div className="grid lg:grid-cols-3 gap-8">
        {/* Form */}
        <div className="lg:col-span-2">
          {step === "shipping" && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <Truck className="h-5 w-5" /> Shipping Information
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <Label htmlFor="firstName">First Name</Label>
                    <Input id="firstName" value={shipping.firstName}
                      onChange={(e) => setShipping({ ...shipping, firstName: e.target.value })} />
                  </div>
                  <div>
                    <Label htmlFor="lastName">Last Name</Label>
                    <Input id="lastName" value={shipping.lastName}
                      onChange={(e) => setShipping({ ...shipping, lastName: e.target.value })} />
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <Label htmlFor="email">Email</Label>
                    <Input id="email" type="email" value={shipping.email}
                      onChange={(e) => setShipping({ ...shipping, email: e.target.value })} />
                  </div>
                  <div>
                    <Label htmlFor="phone">Phone</Label>
                    <Input id="phone" type="tel" value={shipping.phone}
                      onChange={(e) => setShipping({ ...shipping, phone: e.target.value })} />
                  </div>
                </div>
                <div>
                  <Label htmlFor="street">Street Address</Label>
                  <Input id="street" value={shipping.street}
                    onChange={(e) => setShipping({ ...shipping, street: e.target.value })} />
                </div>
                <div>
                  <Label htmlFor="apt">Apt / Suite (optional)</Label>
                  <Input id="apt" value={shipping.apt}
                    onChange={(e) => setShipping({ ...shipping, apt: e.target.value })} />
                </div>
                <div className="grid grid-cols-3 gap-4">
                  <div>
                    <Label htmlFor="city">City</Label>
                    <Input id="city" value={shipping.city}
                      onChange={(e) => setShipping({ ...shipping, city: e.target.value })} />
                  </div>
                  <div>
                    <Label htmlFor="state">State</Label>
                    <Select value={shipping.state} onValueChange={(v) => setShipping({ ...shipping, state: v })}>
                      <SelectTrigger><SelectValue /></SelectTrigger>
                      <SelectContent>
                        {US_STATES.map((s) => (
                          <SelectItem key={s} value={s}>{s}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div>
                    <Label htmlFor="zip">ZIP Code</Label>
                    <Input id="zip" value={shipping.zip}
                      onChange={(e) => setShipping({ ...shipping, zip: e.target.value })} />
                  </div>
                </div>
                <div className="pt-4">
                  <Button onClick={() => setStep("payment")} className="w-full">
                    Continue to Payment
                  </Button>
                </div>
              </CardContent>
            </Card>
          )}

          {step === "payment" && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <CreditCard className="h-5 w-5" /> Payment
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-6">
                <div className="rounded-lg border border-dashed border-brand-300 bg-brand-50 p-6 text-center">
                  <Lock className="h-8 w-8 mx-auto mb-3 text-brand-600" />
                  <p className="font-medium">Stripe Payment Integration</p>
                  <p className="text-sm text-muted-foreground mt-1">
                    Secure payment processing will be handled by Stripe. Configure your Stripe keys to enable.
                  </p>
                </div>
                <div className="flex gap-3">
                  <Button variant="outline" onClick={() => setStep("shipping")}>Back</Button>
                  <Button onClick={() => setStep("review")} className="flex-1">Review Order</Button>
                </div>
              </CardContent>
            </Card>
          )}

          {step === "review" && (
            <Card>
              <CardHeader>
                <CardTitle>Order Review</CardTitle>
              </CardHeader>
              <CardContent className="space-y-6">
                <div>
                  <h3 className="font-medium mb-2">Shipping To</h3>
                  <p className="text-sm text-muted-foreground">
                    {shipping.firstName} {shipping.lastName}<br />
                    {shipping.street}{shipping.apt ? `, ${shipping.apt}` : ""}<br />
                    {shipping.city}, {shipping.state} {shipping.zip}
                  </p>
                </div>
                <Separator />
                <div>
                  <h3 className="font-medium mb-3">Items ({items.length})</h3>
                  <div className="space-y-3">
                    {items.map((item) => (
                      <div key={item.id} className="flex justify-between text-sm">
                        <span>{item.product.name} x{item.quantity}</span>
                        <span>{formatCurrency(Number(item.product.listPrice) * item.quantity)}</span>
                      </div>
                    ))}
                  </div>
                </div>
                <Separator />
                <div className="flex gap-3">
                  <Button variant="outline" onClick={() => setStep("payment")}>Back</Button>
                  <Button className="flex-1">Place Order</Button>
                </div>
              </CardContent>
            </Card>
          )}
        </div>

        {/* Order summary sidebar */}
        <div>
          <Card className="sticky top-24">
            <CardHeader>
              <CardTitle>Order Summary</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              <div className="flex justify-between text-sm">
                <span>Subtotal ({items.length} items)</span>
                <span>{formatCurrency(subtotal)}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span>Shipping</span>
                <span>{shippingCost === 0 ? "Free" : formatCurrency(shippingCost)}</span>
              </div>
              <div className="flex justify-between text-sm">
                <span>Tax (7%)</span>
                <span>{formatCurrency(tax)}</span>
              </div>
              <Separator />
              <div className="flex justify-between font-bold">
                <span>Total</span>
                <span>{formatCurrency(total)}</span>
              </div>
              <div className="flex items-center gap-2 text-xs text-muted-foreground pt-2">
                <Shield className="h-3 w-3" />
                <span>Secure checkout powered by Stripe</span>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
