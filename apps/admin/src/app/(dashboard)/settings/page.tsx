import { Card, CardContent, CardHeader, CardTitle, CardDescription, Input, Label, Button, Separator } from "@overhaul/ui";

export default function SettingsPage() {
  return (
    <div>
      <div className="mb-6">
        <h1 className="text-2xl font-bold">Settings</h1>
        <p className="text-sm text-muted-foreground">Manage your store configuration</p>
      </div>

      <div className="space-y-6 max-w-2xl">
        <Card>
          <CardHeader>
            <CardTitle>Store Information</CardTitle>
            <CardDescription>Basic details about your business</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div><Label>Business Name</Label><Input defaultValue="Overhaul Trading Company LLC" /></div>
            <div><Label>Email</Label><Input defaultValue="hello@overhaultrading.com" /></div>
            <div><Label>Phone</Label><Input placeholder="(555) 123-4567" /></div>
            <div><Label>Address</Label><Input defaultValue="Jacksonville, Florida" /></div>
            <Button>Save Changes</Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Tax Settings</CardTitle>
            <CardDescription>Configure sales tax collection</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <Label>Florida Sales Tax Rate (%)</Label>
              <Input type="number" step="0.01" defaultValue="7.00" />
            </div>
            <Button>Save Tax Settings</Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Shipping</CardTitle>
            <CardDescription>Shipping rates and free shipping threshold</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div><Label>Flat Rate Shipping ($)</Label><Input type="number" step="0.01" defaultValue="9.99" /></div>
            <div><Label>Free Shipping Threshold ($)</Label><Input type="number" step="0.01" defaultValue="100.00" /></div>
            <Button>Save Shipping Settings</Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Consignment</CardTitle>
            <CardDescription>Default consignment terms</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div><Label>Default Commission Rate (%)</Label><Input type="number" step="0.01" defaultValue="30.00" /></div>
            <div><Label>Listing Duration (days)</Label><Input type="number" defaultValue="90" /></div>
            <Button>Save Consignment Settings</Button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
