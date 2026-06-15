"use client";

import { useState } from "react";
import { HandCoins, CheckCircle, DollarSign, Package, TrendingUp } from "lucide-react";
import {
  Button, Card, CardContent, CardHeader, CardTitle, CardDescription,
  Input, Textarea, Label, Badge,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@overhaul/ui";

export default function ConsignmentPage() {
  const [submitted, setSubmitted] = useState(false);

  if (submitted) {
    return (
      <div className="container mx-auto px-4 py-16 text-center max-w-md">
        <CheckCircle className="h-16 w-16 text-green-500 mx-auto mb-4" />
        <h1 className="text-2xl font-bold mb-2">Submission Received!</h1>
        <p className="text-muted-foreground mb-6">
          We&apos;ll review your item and get back to you within 48 hours with next steps.
        </p>
        <Button onClick={() => setSubmitted(false)}>Submit Another Item</Button>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="text-center mb-12">
        <Badge variant="secondary" className="mb-4">Consignment</Badge>
        <h1 className="text-3xl md:text-4xl font-bold mb-4">
          Sell your stuff. <span className="text-green-600">We do the work.</span>
        </h1>
        <p className="text-muted-foreground max-w-xl mx-auto">
          Have quality items you want to sell? Submit them for consignment and we handle
          the listing, selling, and shipping. You get paid when it sells.
        </p>
      </div>

      {/* Benefits */}
      <div className="grid md:grid-cols-3 gap-6 mb-12 max-w-3xl mx-auto">
        <Card className="text-center p-6">
          <DollarSign className="h-8 w-8 text-green-600 mx-auto mb-3" />
          <h3 className="font-semibold mb-1">Fair Commission</h3>
          <p className="text-sm text-muted-foreground">70% to you, 30% to us. Simple and transparent.</p>
        </Card>
        <Card className="text-center p-6">
          <Package className="h-8 w-8 text-green-600 mx-auto mb-3" />
          <h3 className="font-semibold mb-1">We Handle Everything</h3>
          <p className="text-sm text-muted-foreground">Photos, listings, customer service, and shipping.</p>
        </Card>
        <Card className="text-center p-6">
          <TrendingUp className="h-8 w-8 text-green-600 mx-auto mb-3" />
          <h3 className="font-semibold mb-1">Multi-Channel Reach</h3>
          <p className="text-sm text-muted-foreground">Listed on our site, eBay, and Facebook for maximum exposure.</p>
        </Card>
      </div>

      {/* Form */}
      <Card className="max-w-2xl mx-auto">
        <CardHeader>
          <CardTitle>Submit an Item for Consignment</CardTitle>
          <CardDescription>Tell us about your item and we&apos;ll let you know if it&apos;s a good fit.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={(e) => { e.preventDefault(); setSubmitted(true); }} className="space-y-4">
            <div>
              <Label htmlFor="item">Item Description</Label>
              <Input id="item" placeholder="What are you selling?" required />
            </div>
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="category">Category</Label>
                <Select>
                  <SelectTrigger><SelectValue placeholder="Select" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="furniture">Furniture</SelectItem>
                    <SelectItem value="electronics">Electronics</SelectItem>
                    <SelectItem value="collectibles">Collectibles</SelectItem>
                    <SelectItem value="household">Household</SelectItem>
                    <SelectItem value="other">Other</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div>
                <Label htmlFor="condition">Condition</Label>
                <Select>
                  <SelectTrigger><SelectValue placeholder="Select" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="NEW">New</SelectItem>
                    <SelectItem value="LIKE_NEW">Like New</SelectItem>
                    <SelectItem value="EXCELLENT">Excellent</SelectItem>
                    <SelectItem value="GOOD">Good</SelectItem>
                    <SelectItem value="FAIR">Fair</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div>
              <Label htmlFor="value">Estimated Value ($)</Label>
              <Input id="value" type="number" placeholder="What do you think it's worth?" />
            </div>
            <div>
              <Label htmlFor="notes">Additional Notes</Label>
              <Textarea id="notes" placeholder="Any details about the item, its history, etc." rows={3} />
            </div>
            <Button type="submit" size="lg" className="w-full bg-green-600 hover:bg-green-700">
              <HandCoins className="h-4 w-4" />
              Submit for Review
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
