"use client";

import { useState } from "react";
import { Plus, Star, Truck } from "lucide-react";
import {
  Button, Card, CardContent, CardHeader, CardTitle, Badge, formatCurrency, formatDate,
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
  Input, Label, Textarea,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function SuppliersPage() {
  const [addOpen, setAddOpen] = useState(false);
  const { data: suppliers, isLoading } = trpc.suppliers.list.useQuery({});

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Suppliers</h1>
          <p className="text-sm text-muted-foreground">Manage inventory sources and vendor relationships</p>
        </div>
        <Button onClick={() => setAddOpen(true)}>
          <Plus className="h-4 w-4" /> Add Supplier
        </Button>
      </div>

      {isLoading ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {[1, 2, 3].map((i) => (
            <Card key={i} className="animate-pulse"><CardContent className="p-6"><div className="h-20 bg-muted rounded" /></CardContent></Card>
          ))}
        </div>
      ) : suppliers?.length === 0 ? (
        <Card className="p-8 text-center text-muted-foreground">
          No suppliers yet. Add your first inventory source.
        </Card>
      ) : (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {suppliers?.map((supplier) => (
            <Card key={supplier.id} className="hover:shadow-md transition-shadow">
              <CardContent className="p-6">
                <div className="flex items-start justify-between mb-3">
                  <div>
                    <h3 className="font-semibold">{supplier.name}</h3>
                    <Badge variant="outline" className="text-xs mt-1">
                      {supplier.type.replace(/_/g, " ")}
                    </Badge>
                  </div>
                  {supplier.rating && (
                    <div className="flex items-center gap-1">
                      <Star className="h-3 w-3 fill-yellow-400 text-yellow-400" />
                      <span className="text-sm font-medium">{supplier.rating}</span>
                    </div>
                  )}
                </div>
                <div className="space-y-1 text-sm text-muted-foreground">
                  {supplier.contactName && <p>{supplier.contactName}</p>}
                  {supplier.email && <p>{supplier.email}</p>}
                  {supplier.phone && <p>{supplier.phone}</p>}
                </div>
                <div className="grid grid-cols-2 gap-4 mt-4 pt-4 border-t">
                  <div>
                    <p className="text-xs text-muted-foreground">Total Spent</p>
                    <p className="font-bold">{formatCurrency(Number(supplier.totalSpent))}</p>
                  </div>
                  <div>
                    <p className="text-xs text-muted-foreground">Items Sourced</p>
                    <p className="font-bold">{supplier.totalItems}</p>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      <Dialog open={addOpen} onOpenChange={setAddOpen}>
        <DialogContent>
          <DialogHeader><DialogTitle>Add Supplier</DialogTitle></DialogHeader>
          <form className="space-y-4">
            <div>
              <Label>Supplier Name</Label>
              <Input placeholder="e.g., Jacksonville Estate Sales" />
            </div>
            <div>
              <Label>Type</Label>
              <Select>
                <SelectTrigger><SelectValue placeholder="Select type" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="THRIFT_STORE">Thrift Store</SelectItem>
                  <SelectItem value="ESTATE_COMPANY">Estate Company</SelectItem>
                  <SelectItem value="LIQUIDATION_WHOLESALER">Liquidation Wholesaler</SelectItem>
                  <SelectItem value="AUCTION_HOUSE">Auction House</SelectItem>
                  <SelectItem value="PRIVATE_SELLER">Private Seller</SelectItem>
                  <SelectItem value="BUSINESS_CLOSURE">Business Closure</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="grid sm:grid-cols-2 gap-4">
              <div><Label>Contact Name</Label><Input placeholder="Contact person" /></div>
              <div><Label>Email</Label><Input type="email" placeholder="email@example.com" /></div>
            </div>
            <div><Label>Phone</Label><Input placeholder="(555) 123-4567" /></div>
            <div><Label>Notes</Label><Textarea placeholder="Notes about this supplier..." rows={2} /></div>
          </form>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAddOpen(false)}>Cancel</Button>
            <Button onClick={() => setAddOpen(false)}>Save Supplier</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
