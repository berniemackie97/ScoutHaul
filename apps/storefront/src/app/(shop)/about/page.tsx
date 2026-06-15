import { Package, Recycle, Target, Users, MapPin, Mail, Phone } from "lucide-react";
import { Card, CardContent, Badge, Separator } from "@overhaul/ui";

export default function AboutPage() {
  return (
    <div className="container mx-auto px-4 py-8">
      {/* Hero */}
      <div className="text-center mb-12 max-w-2xl mx-auto">
        <Badge variant="secondary" className="mb-4">About Us</Badge>
        <h1 className="text-3xl md:text-4xl font-bold mb-4">
          Quality secondhand goods deserve a <span className="text-brand-600">second life</span>.
        </h1>
        <p className="text-muted-foreground">
          Overhaul Trading Company LLC is a Florida-based resale business specializing
          in sourcing and selling quality secondhand goods. We source strategically,
          price fairly, and stand behind everything we sell.
        </p>
      </div>

      {/* Mission / Vision / Values */}
      <div className="grid md:grid-cols-3 gap-6 mb-16">
        <Card className="p-6">
          <Target className="h-8 w-8 text-brand-600 mb-4" />
          <h3 className="font-bold text-lg mb-2">Our Mission</h3>
          <p className="text-sm text-muted-foreground">
            Make quality secondhand goods accessible and affordable while maximizing
            the lifecycle value of inventory through multiple sales channels.
          </p>
        </Card>
        <Card className="p-6">
          <Recycle className="h-8 w-8 text-green-600 mb-4" />
          <h3 className="font-bold text-lg mb-2">Sustainability</h3>
          <p className="text-sm text-muted-foreground">
            Resale reduces waste, serves budget-conscious buyers, and creates
            sustainable margins. We extend the life of goods that deserve another chapter.
          </p>
        </Card>
        <Card className="p-6">
          <Users className="h-8 w-8 text-blue-600 mb-4" />
          <h3 className="font-bold text-lg mb-2">Community First</h3>
          <p className="text-sm text-muted-foreground">
            We serve the Jacksonville and Florida community with fair pricing,
            honest descriptions, and reliable service across every transaction.
          </p>
        </Card>
      </div>

      {/* What we do */}
      <div className="max-w-3xl mx-auto mb-16">
        <h2 className="text-2xl font-bold text-center mb-8">What We Do</h2>
        <div className="space-y-6">
          <div className="flex gap-4">
            <div className="h-10 w-10 rounded-lg bg-brand-100 flex items-center justify-center shrink-0">
              <Package className="h-5 w-5 text-brand-600" />
            </div>
            <div>
              <h3 className="font-semibold">Online Resale</h3>
              <p className="text-sm text-muted-foreground">
                We source quality items from estate sales, thrift stores, and liquidation
                events, then sell them on our website, eBay, and Facebook Marketplace.
              </p>
            </div>
          </div>
          <Separator />
          <div className="flex gap-4">
            <div className="h-10 w-10 rounded-lg bg-brand-100 flex items-center justify-center shrink-0">
              <span className="text-xl">🔧</span>
            </div>
            <div>
              <h3 className="font-semibold">Repair Services</h3>
              <p className="text-sm text-muted-foreground">
                Professional repair for electronics, furniture, and appliances.
                Don&apos;t replace it — let us fix it at a fair price.
              </p>
            </div>
          </div>
          <Separator />
          <div className="flex gap-4">
            <div className="h-10 w-10 rounded-lg bg-brand-100 flex items-center justify-center shrink-0">
              <span className="text-xl">🤝</span>
            </div>
            <div>
              <h3 className="font-semibold">Consignment & Wholesale</h3>
              <p className="text-sm text-muted-foreground">
                Have items to sell? We consign quality goods and also work with
                retailers and resellers on wholesale deals.
              </p>
            </div>
          </div>
        </div>
      </div>

      {/* Contact */}
      <div id="contact" className="max-w-md mx-auto text-center">
        <h2 className="text-2xl font-bold mb-6">Get in Touch</h2>
        <Card className="p-6 space-y-4">
          <div className="flex items-center justify-center gap-2 text-sm">
            <MapPin className="h-4 w-4 text-muted-foreground" />
            <span>Jacksonville, Florida</span>
          </div>
          <div className="flex items-center justify-center gap-2 text-sm">
            <Mail className="h-4 w-4 text-muted-foreground" />
            <span>hello@overhaultrading.com</span>
          </div>
        </Card>
      </div>
    </div>
  );
}
