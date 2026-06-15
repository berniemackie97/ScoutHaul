import { PrismaClient } from "@prisma/client";

const prisma = new PrismaClient();

async function main() {
  console.log("Seeding database...");

  // Categories
  const categories = await Promise.all([
    prisma.category.upsert({
      where: { slug: "furniture" },
      update: {},
      create: { name: "Furniture", slug: "furniture", sortOrder: 1, description: "Tables, chairs, desks, shelving, and more" },
    }),
    prisma.category.upsert({
      where: { slug: "electronics" },
      update: {},
      create: { name: "Electronics", slug: "electronics", sortOrder: 2, description: "Laptops, phones, tablets, and accessories" },
    }),
    prisma.category.upsert({
      where: { slug: "collectibles" },
      update: {},
      create: { name: "Collectibles", slug: "collectibles", sortOrder: 3, description: "Vintage items, memorabilia, and rare finds" },
    }),
    prisma.category.upsert({
      where: { slug: "household" },
      update: {},
      create: { name: "Household", slug: "household", sortOrder: 4, description: "Kitchen, decor, appliances, and home goods" },
    }),
    prisma.category.upsert({
      where: { slug: "sporting-goods" },
      update: {},
      create: { name: "Sporting Goods", slug: "sporting-goods", sortOrder: 5, description: "Fitness equipment, outdoor gear, and sports items" },
    }),
    prisma.category.upsert({
      where: { slug: "tools-equipment" },
      update: {},
      create: { name: "Tools & Equipment", slug: "tools-equipment", sortOrder: 6, description: "Power tools, hand tools, and workshop equipment" },
    }),
  ]);

  console.log(`Created ${categories.length} categories`);

  // Sample products
  const sampleProducts = [
    {
      sku: "OT-SEED-001",
      name: "Mid-Century Modern Teak Writing Desk",
      slug: "mid-century-modern-teak-writing-desk",
      description: "Beautiful mid-century teak writing desk with two drawers and tapered legs. Minor surface wear consistent with age. Solid construction, all joints tight. Dimensions: 48\"W x 24\"D x 30\"H.",
      shortDescription: "Stunning teak desk with classic mid-century lines",
      condition: "GOOD" as const,
      conditionNotes: "Minor surface scratches, one small ring mark on top. Structurally perfect.",
      categoryId: categories[0]!.id,
      costPrice: 45,
      listPrice: 129,
      compareAtPrice: 175,
      sourceType: "ESTATE_SALE" as const,
      brand: "Unknown",
      tags: ["desk", "mid-century", "teak", "vintage"],
      isFeatured: true,
      status: "ACTIVE" as const,
    },
    {
      sku: "OT-SEED-002",
      name: "Apple MacBook Air M1 (2020) - Space Gray",
      slug: "apple-macbook-air-m1-2020-space-gray",
      description: "Apple MacBook Air with M1 chip, 8GB RAM, 256GB SSD. Battery health 89%. Minor cosmetic wear on bottom case. Runs perfectly, comes with charger. macOS Sequoia installed.",
      shortDescription: "M1 MacBook Air in great working condition",
      condition: "GOOD" as const,
      conditionNotes: "Light scratches on bottom case. Screen perfect. Battery health 89%.",
      categoryId: categories[1]!.id,
      costPrice: 280,
      listPrice: 549,
      compareAtPrice: 699,
      sourceType: "LIQUIDATION_PALLET" as const,
      brand: "Apple",
      model: "MacBook Air",
      year: 2020,
      tags: ["laptop", "apple", "macbook", "m1"],
      isFeatured: true,
      status: "ACTIVE" as const,
    },
    {
      sku: "OT-SEED-003",
      name: "Vintage Pyrex Mixing Bowl Set - Primary Colors",
      slug: "vintage-pyrex-mixing-bowl-set-primary-colors",
      description: "Complete set of 4 nesting Pyrex mixing bowls in primary colors (blue, red, green, yellow). All four bowls present, no chips or cracks. Colors vibrant with minor fade on red bowl.",
      shortDescription: "Complete primary colors Pyrex set - great condition",
      condition: "EXCELLENT" as const,
      categoryId: categories[2]!.id,
      costPrice: 15,
      listPrice: 89,
      sourceType: "THRIFT_STORE" as const,
      brand: "Pyrex",
      tags: ["pyrex", "vintage", "kitchen", "collectible"],
      isFeatured: true,
      status: "ACTIVE" as const,
    },
    {
      sku: "OT-SEED-004",
      name: "KitchenAid Professional 600 Stand Mixer - Silver",
      slug: "kitchenaid-professional-600-stand-mixer-silver",
      description: "KitchenAid Professional 600 Series 6-quart stand mixer. Powerful 575-watt motor. Includes flat beater, dough hook, and wire whip. Bowl shows normal use. Motor runs strong.",
      shortDescription: "Pro-grade KitchenAid mixer at a fraction of retail",
      condition: "GOOD" as const,
      conditionNotes: "Bowl has minor scratches. Motor and all attachments work perfectly.",
      categoryId: categories[3]!.id,
      costPrice: 55,
      listPrice: 159,
      compareAtPrice: 350,
      sourceType: "ESTATE_SALE" as const,
      brand: "KitchenAid",
      model: "Professional 600",
      tags: ["mixer", "kitchen", "kitchenaid", "appliance"],
      isFeatured: true,
      status: "ACTIVE" as const,
    },
    {
      sku: "OT-SEED-005",
      name: "DeWalt 20V Max Cordless Drill/Driver Kit",
      slug: "dewalt-20v-max-cordless-drill-driver-kit",
      description: "DeWalt DCD771C2 20V Max cordless drill with two batteries and charger. Both batteries hold charge. Chuck and clutch in great working order.",
      shortDescription: "Reliable DeWalt drill kit with 2 batteries",
      condition: "GOOD" as const,
      categoryId: categories[5]!.id,
      costPrice: 22,
      listPrice: 65,
      compareAtPrice: 99,
      sourceType: "THRIFT_STORE" as const,
      brand: "DeWalt",
      model: "DCD771C2",
      tags: ["drill", "dewalt", "power-tool", "cordless"],
      status: "ACTIVE" as const,
    },
  ];

  for (const product of sampleProducts) {
    await prisma.product.upsert({
      where: { sku: product.sku },
      update: {},
      create: product,
    });
  }

  console.log(`Created ${sampleProducts.length} sample products`);

  // Sample blog post
  await prisma.blogPost.upsert({
    where: { slug: "welcome-to-overhaul-trading" },
    update: {},
    create: {
      title: "Welcome to Overhaul Trading Company",
      slug: "welcome-to-overhaul-trading",
      excerpt: "We're launching our online store to bring quality secondhand finds directly to you.",
      content: "We're excited to launch the Overhaul Trading Company online store! For years, we've been sourcing quality secondhand goods from estate sales, thrift stores, and liquidation events across Florida. Now, we're bringing those finds directly to you.\n\nWhat sets us apart? We inspect every item, describe it honestly, and price it fairly. No surprises, no gimmicks — just quality secondhand goods that deserve a second life.\n\nBrowse our growing collection of furniture, electronics, collectibles, and more. And if you have something that needs fixing, check out our repair services too.",
      authorName: "Overhaul Trading Team",
      tags: ["announcement", "company"],
      isPublished: true,
      publishedAt: new Date(),
    },
  });

  console.log("Created sample blog post");
  console.log("Seed complete!");
}

main()
  .catch((e) => {
    console.error(e);
    process.exit(1);
  })
  .finally(async () => {
    await prisma.$disconnect();
  });
