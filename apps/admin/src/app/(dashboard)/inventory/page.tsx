"use client";

import { useState } from "react";
import Link from "next/link";
import Image from "next/image";
import { Plus, Search, Filter, MoreHorizontal, Eye, Edit, Trash2, ExternalLink } from "lucide-react";
import {
  Button, Input, Card, Badge, Separator, formatCurrency,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, DropdownMenuSeparator,
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
  Label, Textarea,
} from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

const statusColors: Record<string, string> = {
  DRAFT: "secondary",
  ACTIVE: "success",
  RESERVED: "warning",
  SOLD: "default",
  ARCHIVED: "destructive",
  REPAIR_NEEDED: "info",
};

export default function InventoryPage() {
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [addDialogOpen, setAddDialogOpen] = useState(false);

  const { data, isLoading } = trpc.products.list.useQuery({
    search: search || undefined,
    limit: 50,
  });

  const { data: categories } = trpc.products.categories.useQuery();

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Inventory</h1>
          <p className="text-sm text-muted-foreground">Manage products across all channels</p>
        </div>
        <Button onClick={() => setAddDialogOpen(true)}>
          <Plus className="h-4 w-4" /> Add Product
        </Button>
      </div>

      {/* Filters */}
      <div className="flex gap-3 mb-6">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            placeholder="Search inventory..."
            className="pl-10"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="All Status" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All Status</SelectItem>
            <SelectItem value="DRAFT">Draft</SelectItem>
            <SelectItem value="ACTIVE">Active</SelectItem>
            <SelectItem value="RESERVED">Reserved</SelectItem>
            <SelectItem value="SOLD">Sold</SelectItem>
            <SelectItem value="ARCHIVED">Archived</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Product Table */}
      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Product</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">SKU</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Status</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Condition</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Cost</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Price</th>
                <th className="text-right text-xs font-medium text-muted-foreground p-3">Margin</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Source</th>
                <th className="text-center text-xs font-medium text-muted-foreground p-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i} className="border-b">
                    <td colSpan={9} className="p-3">
                      <div className="h-10 bg-muted rounded animate-pulse" />
                    </td>
                  </tr>
                ))
              ) : data?.items.length === 0 ? (
                <tr>
                  <td colSpan={9} className="p-8 text-center text-muted-foreground">
                    No products found. Add your first product to get started.
                  </td>
                </tr>
              ) : (
                data?.items.map((product) => {
                  const cost = Number(product.costPrice);
                  const price = Number(product.listPrice);
                  const margin = price > 0 ? ((price - cost) / price * 100).toFixed(0) : "0";
                  return (
                    <tr key={product.id} className="border-b hover:bg-muted/30 transition-colors">
                      <td className="p-3">
                        <div className="flex items-center gap-3">
                          <div className="relative h-10 w-10 rounded bg-muted overflow-hidden shrink-0">
                            {product.images[0] && (
                              <Image src={product.images[0].url} alt="" fill className="object-cover" />
                            )}
                          </div>
                          <div className="min-w-0">
                            <p className="text-sm font-medium truncate max-w-[200px]">{product.name}</p>
                            <p className="text-xs text-muted-foreground">{product.category.name}</p>
                          </div>
                        </div>
                      </td>
                      <td className="p-3 text-xs font-mono text-muted-foreground">{product.sku}</td>
                      <td className="p-3">
                        <Badge variant={statusColors[product.status] as any}>{product.status}</Badge>
                      </td>
                      <td className="p-3 text-xs">{product.condition.replace(/_/g, " ")}</td>
                      <td className="p-3 text-right text-sm">{formatCurrency(cost)}</td>
                      <td className="p-3 text-right text-sm font-medium">{formatCurrency(price)}</td>
                      <td className="p-3 text-right">
                        <span className={`text-sm font-medium ${Number(margin) >= 50 ? "text-green-600" : Number(margin) >= 30 ? "text-yellow-600" : "text-red-600"}`}>
                          {margin}%
                        </span>
                      </td>
                      <td className="p-3 text-xs text-muted-foreground">{product.sourceType.replace(/_/g, " ")}</td>
                      <td className="p-3 text-center">
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon" className="h-8 w-8">
                              <MoreHorizontal className="h-4 w-4" />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuItem>
                              <Eye className="h-4 w-4" /> View
                            </DropdownMenuItem>
                            <DropdownMenuItem>
                              <Edit className="h-4 w-4" /> Edit
                            </DropdownMenuItem>
                            <DropdownMenuItem>
                              <ExternalLink className="h-4 w-4" /> View on Store
                            </DropdownMenuItem>
                            <DropdownMenuSeparator />
                            <DropdownMenuItem className="text-red-600">
                              <Trash2 className="h-4 w-4" /> Archive
                            </DropdownMenuItem>
                          </DropdownMenuContent>
                        </DropdownMenu>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Add Product Dialog */}
      <Dialog open={addDialogOpen} onOpenChange={setAddDialogOpen}>
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>Add New Product</DialogTitle>
            <DialogDescription>Add a new item to your inventory</DialogDescription>
          </DialogHeader>
          <form className="space-y-4">
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <Label>Product Name</Label>
                <Input placeholder="e.g., Mid-Century Modern Desk" />
              </div>
              <div>
                <Label>Category</Label>
                <Select>
                  <SelectTrigger><SelectValue placeholder="Select" /></SelectTrigger>
                  <SelectContent>
                    {categories?.map((cat) => (
                      <SelectItem key={cat.id} value={cat.id}>{cat.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div>
              <Label>Description</Label>
              <Textarea placeholder="Describe the item in detail..." rows={3} />
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div>
                <Label>Cost Price ($)</Label>
                <Input type="number" step="0.01" placeholder="0.00" />
              </div>
              <div>
                <Label>List Price ($)</Label>
                <Input type="number" step="0.01" placeholder="0.00" />
              </div>
              <div>
                <Label>Condition</Label>
                <Select>
                  <SelectTrigger><SelectValue placeholder="Select" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="NEW">New</SelectItem>
                    <SelectItem value="LIKE_NEW">Like New</SelectItem>
                    <SelectItem value="EXCELLENT">Excellent</SelectItem>
                    <SelectItem value="GOOD">Good</SelectItem>
                    <SelectItem value="FAIR">Fair</SelectItem>
                    <SelectItem value="POOR">Poor</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <div className="grid sm:grid-cols-2 gap-4">
              <div>
                <Label>Source Type</Label>
                <Select>
                  <SelectTrigger><SelectValue placeholder="Select" /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="THRIFT_STORE">Thrift Store</SelectItem>
                    <SelectItem value="ESTATE_SALE">Estate Sale</SelectItem>
                    <SelectItem value="LIQUIDATION_PALLET">Liquidation Pallet</SelectItem>
                    <SelectItem value="CONSIGNMENT">Consignment</SelectItem>
                    <SelectItem value="PAWN">Pawn</SelectItem>
                    <SelectItem value="WHOLESALE">Wholesale</SelectItem>
                    <SelectItem value="PRIVATE_PURCHASE">Private Purchase</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div>
                <Label>Brand (optional)</Label>
                <Input placeholder="e.g., Samsung, IKEA" />
              </div>
            </div>
          </form>
          <DialogFooter>
            <Button variant="outline" onClick={() => setAddDialogOpen(false)}>Cancel</Button>
            <Button onClick={() => setAddDialogOpen(false)}>Save Product</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
