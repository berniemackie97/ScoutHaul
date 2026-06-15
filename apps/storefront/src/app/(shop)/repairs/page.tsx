"use client";

import { useState } from "react";
import { Wrench, Clock, DollarSign, CheckCircle, Upload } from "lucide-react";
import {
  Button, Card, CardContent, CardHeader, CardTitle, CardDescription,
  Input, Textarea, Label, Badge,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@overhaul/ui";

const repairTypes = [
  { icon: "💻", title: "Electronics", description: "Laptops, phones, tablets, gaming consoles" },
  { icon: "🪑", title: "Furniture", description: "Chairs, tables, dressers, shelving" },
  { icon: "🔌", title: "Appliances", description: "Small appliances, kitchen equipment" },
  { icon: "🔧", title: "General", description: "Tools, equipment, miscellaneous items" },
];

const processSteps = [
  { step: 1, title: "Submit Request", description: "Tell us what needs fixing" },
  { step: 2, title: "Get a Quote", description: "We diagnose and send you a price" },
  { step: 3, title: "Approve & Repair", description: "We fix it once you approve" },
  { step: 4, title: "Pick Up", description: "Grab your repaired item" },
];

export default function RepairsPage() {
  const [submitted, setSubmitted] = useState(false);

  if (submitted) {
    return (
      <div className="container mx-auto px-4 py-16 text-center max-w-md">
        <CheckCircle className="h-16 w-16 text-green-500 mx-auto mb-4" />
        <h1 className="text-2xl font-bold mb-2">Request Submitted!</h1>
        <p className="text-muted-foreground mb-6">
          We&apos;ll review your repair request and send you a quote within 24-48 hours.
        </p>
        <Button onClick={() => setSubmitted(false)}>Submit Another Request</Button>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4 py-8">
      {/* Hero */}
      <div className="text-center mb-12">
        <Badge variant="secondary" className="mb-4">Repair Services</Badge>
        <h1 className="text-3xl md:text-4xl font-bold mb-4">
          Don&apos;t replace it. <span className="text-brand-600">Repair it.</span>
        </h1>
        <p className="text-muted-foreground max-w-xl mx-auto">
          Professional repair services for electronics, furniture, and appliances.
          Fair pricing, quality workmanship, and fast turnaround.
        </p>
      </div>

      {/* What we repair */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-12">
        {repairTypes.map((type) => (
          <Card key={type.title} className="text-center p-6">
            <div className="text-4xl mb-3">{type.icon}</div>
            <h3 className="font-semibold">{type.title}</h3>
            <p className="text-xs text-muted-foreground mt-1">{type.description}</p>
          </Card>
        ))}
      </div>

      {/* Process */}
      <div className="mb-12">
        <h2 className="text-2xl font-bold text-center mb-8">How It Works</h2>
        <div className="grid md:grid-cols-4 gap-6">
          {processSteps.map((step) => (
            <div key={step.step} className="text-center">
              <div className="h-12 w-12 rounded-full bg-brand-100 dark:bg-brand-900 text-brand-600 flex items-center justify-center mx-auto mb-3 font-bold">
                {step.step}
              </div>
              <h3 className="font-semibold mb-1">{step.title}</h3>
              <p className="text-sm text-muted-foreground">{step.description}</p>
            </div>
          ))}
        </div>
      </div>

      {/* Request Form */}
      <Card className="max-w-2xl mx-auto">
        <CardHeader>
          <CardTitle>Request a Repair Quote</CardTitle>
          <CardDescription>Fill out the form below and we&apos;ll get back to you within 24-48 hours.</CardDescription>
        </CardHeader>
        <CardContent>
          <form
            onSubmit={(e) => { e.preventDefault(); setSubmitted(true); }}
            className="space-y-4"
          >
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="item">Item Description</Label>
                <Input id="item" placeholder="e.g., Samsung Galaxy S24" required />
              </div>
              <div>
                <Label htmlFor="category">Category</Label>
                <Select>
                  <SelectTrigger>
                    <SelectValue placeholder="Select category" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="electronics">Electronics</SelectItem>
                    <SelectItem value="furniture">Furniture</SelectItem>
                    <SelectItem value="appliances">Appliances</SelectItem>
                    <SelectItem value="general">General</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="brand">Brand (optional)</Label>
                <Input id="brand" placeholder="e.g., Samsung" />
              </div>
              <div>
                <Label htmlFor="model">Model (optional)</Label>
                <Input id="model" placeholder="e.g., Galaxy S24" />
              </div>
            </div>
            <div>
              <Label htmlFor="issue">What&apos;s wrong with it?</Label>
              <Textarea
                id="issue"
                placeholder="Describe the issue in detail..."
                rows={4}
                required
              />
            </div>
            <div>
              <Label htmlFor="priority">Priority</Label>
              <Select defaultValue="NORMAL">
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="LOW">Low - No rush</SelectItem>
                  <SelectItem value="NORMAL">Normal - Standard turnaround</SelectItem>
                  <SelectItem value="HIGH">High - Need it soon</SelectItem>
                  <SelectItem value="RUSH">Rush - ASAP (additional fee)</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button type="submit" size="lg" className="w-full">
              <Wrench className="h-4 w-4" />
              Submit Repair Request
            </Button>
          </form>
        </CardContent>
      </Card>

      {/* Pricing note */}
      <div className="text-center mt-8 text-sm text-muted-foreground max-w-md mx-auto">
        <p>
          All quotes are free with no obligation. We only proceed with your
          approval. Most repairs completed within 3-5 business days.
        </p>
      </div>
    </div>
  );
}
