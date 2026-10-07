using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HaulScout.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Synced items carried the marketplace's own words: categories no profile knows ("Tops", "CCG Individual
    /// Cards"), conditions off the list ("Very good", Poshmark's "ug"), and sizes with no system ("L / US 12-14"). The
    /// editor showed each as blank, and saving the item cleared it. Each is rewritten in the editor's terms where it can
    /// be read — category first, since what a size means depends on it — and left as it was where it cannot. The
    /// spellings are the domain vocabularies' as of this migration; the API applies the live ones from here on.
    /// </summary>
    public partial class SyncedDetailsInTheEditorsTerms : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Categories: a match read from the leaf of the name toward its root, else Other. An organization's own are kept.
            migrationBuilder.Sql("""
                WITH spellings(spelling, category) AS (
                    VALUES
                    ('clothing', 'Clothing'),
                    ('baby', 'Baby'),
                    ('shoes', 'Shoes'),
                    ('handbags', 'Handbags'),
                    ('jewelry', 'Jewelry'),
                    ('watches', 'Watches'),
                    ('tech', 'Tech'),
                    ('phones', 'Phones'),
                    ('cameras', 'Cameras'),
                    ('audio', 'Audio'),
                    ('tv', 'TV'),
                    ('electronics', 'Electronics'),
                    ('appliances', 'Appliances'),
                    ('video games', 'Video Games'),
                    ('collectibles', 'Collectibles'),
                    ('toys', 'Toys'),
                    ('books', 'Books'),
                    ('media', 'Media'),
                    ('tools', 'Tools'),
                    ('garden', 'Garden'),
                    ('fitness', 'Fitness'),
                    ('musical instruments', 'Musical Instruments'),
                    ('automotive', 'Automotive'),
                    ('industrial', 'Industrial'),
                    ('office', 'Office'),
                    ('sporting goods', 'Sporting Goods'),
                    ('furniture', 'Furniture'),
                    ('decor', 'Decor'),
                    ('home', 'Home'),
                    ('crafts', 'Crafts'),
                    ('health beauty', 'Health & Beauty'),
                    ('pet supplies', 'Pet Supplies'),
                    ('other', 'Other'),
                    ('clothes', 'Clothing'),
                    ('apparel', 'Clothing'),
                    ('tops', 'Clothing'),
                    ('shirts', 'Clothing'),
                    ('t shirts', 'Clothing'),
                    ('tees', 'Clothing'),
                    ('blouses', 'Clothing'),
                    ('polos', 'Clothing'),
                    ('sweaters', 'Clothing'),
                    ('cardigans', 'Clothing'),
                    ('hoodies', 'Clothing'),
                    ('sweatshirts', 'Clothing'),
                    ('jackets', 'Clothing'),
                    ('coats', 'Clothing'),
                    ('vests', 'Clothing'),
                    ('blazers', 'Clothing'),
                    ('outerwear', 'Clothing'),
                    ('dresses', 'Clothing'),
                    ('skirts', 'Clothing'),
                    ('jeans', 'Clothing'),
                    ('pants', 'Clothing'),
                    ('trousers', 'Clothing'),
                    ('shorts', 'Clothing'),
                    ('leggings', 'Clothing'),
                    ('jumpsuits', 'Clothing'),
                    ('rompers', 'Clothing'),
                    ('overalls', 'Clothing'),
                    ('suits', 'Clothing'),
                    ('swimwear', 'Clothing'),
                    ('swim', 'Clothing'),
                    ('sleepwear', 'Clothing'),
                    ('robes', 'Clothing'),
                    ('pajamas', 'Clothing'),
                    ('lingerie', 'Clothing'),
                    ('intimates', 'Clothing'),
                    ('activewear', 'Clothing'),
                    ('uniforms', 'Clothing'),
                    ('clothing shoes accessories', 'Clothing'),
                    ('footwear', 'Shoes'),
                    ('sneakers', 'Shoes'),
                    ('boots', 'Shoes'),
                    ('heels', 'Shoes'),
                    ('pumps', 'Shoes'),
                    ('sandals', 'Shoes'),
                    ('flats', 'Shoes'),
                    ('loafers', 'Shoes'),
                    ('slippers', 'Shoes'),
                    ('wedges', 'Shoes'),
                    ('mules', 'Shoes'),
                    ('clogs', 'Shoes'),
                    ('oxfords', 'Shoes'),
                    ('bags', 'Handbags'),
                    ('handbag', 'Handbags'),
                    ('purses', 'Handbags'),
                    ('backpacks', 'Handbags'),
                    ('totes', 'Handbags'),
                    ('clutches', 'Handbags'),
                    ('wallets', 'Handbags'),
                    ('crossbody', 'Handbags'),
                    ('satchels', 'Handbags'),
                    ('accessories', 'Handbags'),
                    ('hats', 'Handbags'),
                    ('belts', 'Handbags'),
                    ('scarves', 'Handbags'),
                    ('sunglasses', 'Handbags'),
                    ('jewellery', 'Jewelry'),
                    ('necklaces', 'Jewelry'),
                    ('rings', 'Jewelry'),
                    ('earrings', 'Jewelry'),
                    ('bracelets', 'Jewelry'),
                    ('brooches', 'Jewelry'),
                    ('pendants', 'Jewelry'),
                    ('watch', 'Watches'),
                    ('wristwatches', 'Watches'),
                    ('collectible', 'Collectibles'),
                    ('figures', 'Collectibles'),
                    ('figurines', 'Collectibles'),
                    ('bobbleheads', 'Collectibles'),
                    ('statues', 'Collectibles'),
                    ('memorabilia', 'Collectibles'),
                    ('trading cards', 'Collectibles'),
                    ('cards', 'Collectibles'),
                    ('ccg', 'Collectibles'),
                    ('tcg', 'Collectibles'),
                    ('coins', 'Collectibles'),
                    ('stamps', 'Collectibles'),
                    ('autographs', 'Collectibles'),
                    ('pins', 'Collectibles'),
                    ('toy', 'Toys'),
                    ('lego', 'Toys'),
                    ('dolls', 'Toys'),
                    ('plush', 'Toys'),
                    ('action figures', 'Toys'),
                    ('puzzles', 'Toys'),
                    ('board games', 'Toys'),
                    ('video game', 'Video Games'),
                    ('consoles', 'Video Games'),
                    ('controllers', 'Video Games'),
                    ('gaming', 'Video Games'),
                    ('kitchen', 'Home'),
                    ('kitchenware', 'Home'),
                    ('mugs', 'Home'),
                    ('cups', 'Home'),
                    ('dinnerware', 'Home'),
                    ('tableware', 'Home'),
                    ('glassware', 'Home'),
                    ('cookware', 'Home'),
                    ('bedding', 'Home'),
                    ('bath', 'Home'),
                    ('housewares', 'Home'),
                    ('art', 'Decor'),
                    ('wall art', 'Decor'),
                    ('vases', 'Decor'),
                    ('candles', 'Decor'),
                    ('frames', 'Decor'),
                    ('rugs', 'Decor'),
                    ('chairs', 'Furniture'),
                    ('tables', 'Furniture'),
                    ('desks', 'Furniture'),
                    ('shelves', 'Furniture'),
                    ('computers', 'Tech'),
                    ('computer', 'Tech'),
                    ('laptops', 'Tech'),
                    ('tablets', 'Tech'),
                    ('cell phones', 'Phones'),
                    ('smartphones', 'Phones'),
                    ('headphones', 'Audio'),
                    ('speakers', 'Audio'),
                    ('earbuds', 'Audio'),
                    ('camera', 'Cameras'),
                    ('lenses', 'Cameras'),
                    ('photography', 'Cameras'),
                    ('bath body', 'Health & Beauty'),
                    ('beauty', 'Health & Beauty'),
                    ('makeup', 'Health & Beauty'),
                    ('skincare', 'Health & Beauty'),
                    ('fragrance', 'Health & Beauty'),
                    ('fragrances', 'Health & Beauty'),
                    ('cosmetics', 'Health & Beauty'),
                    ('hair care', 'Health & Beauty'),
                    ('sports', 'Sporting Goods'),
                    ('golf', 'Sporting Goods'),
                    ('fishing', 'Sporting Goods'),
                    ('camping', 'Sporting Goods'),
                    ('cycling', 'Sporting Goods'),
                    ('exercise', 'Fitness'),
                    ('yoga', 'Fitness'),
                    ('book', 'Books'),
                    ('magazines', 'Books'),
                    ('comics', 'Books'),
                    ('textbooks', 'Books'),
                    ('dvds', 'Media'),
                    ('dvd', 'Media'),
                    ('vinyl', 'Media'),
                    ('records', 'Media'),
                    ('cds', 'Media'),
                    ('movies', 'Media'),
                    ('pet', 'Pet Supplies'),
                    ('pets', 'Pet Supplies'),
                    ('craft', 'Crafts'),
                    ('sewing', 'Crafts'),
                    ('yarn', 'Crafts'),
                    ('tool', 'Tools'),
                    ('hardware', 'Tools')),
                candidates AS (
                    SELECT item."Id", item."Category"
                    FROM "InventoryItems" item
                    WHERE btrim(coalesce(item."Category", '')) <> ''
                      AND lower(item."Category") NOT IN ('clothing', 'baby', 'shoes', 'handbags', 'jewelry', 'watches', 'tech', 'phones', 'cameras', 'audio', 'tv', 'electronics', 'appliances', 'video games', 'collectibles', 'toys', 'books', 'media', 'tools', 'garden', 'fitness', 'musical instruments', 'automotive', 'industrial', 'office', 'sporting goods', 'furniture', 'decor', 'home', 'crafts', 'health & beauty', 'pet supplies', 'other')
                      AND NOT EXISTS (
                          SELECT 1 FROM "OrgReferenceOverrides" custom
                          WHERE custom."OrganizationId" = item."OrganizationId"
                            AND lower(custom."Kind") = 'category'
                            AND custom."IsCustom"
                            AND lower(custom."Value") = lower(item."Category"))),
                segments AS (
                    SELECT candidates."Id", segment.depth, ' ' || btrim(regexp_replace(lower(segment.part), '[^a-z0-9]+', ' ', 'g')) || ' ' AS words
                    FROM candidates
                    CROSS JOIN LATERAL regexp_split_to_table(candidates."Category", '[>›»]') WITH ORDINALITY AS segment(part, depth)),
                matches AS (
                    SELECT DISTINCT ON (segments."Id") segments."Id", spellings.category
                    FROM segments
                    JOIN spellings ON segments.words LIKE '% ' || spellings.spelling || ' %'
                    ORDER BY segments."Id", segments.depth DESC, length(spellings.spelling) DESC, position(' ' || spellings.spelling || ' ' IN segments.words))
                UPDATE "InventoryItems" item
                SET "Category" = coalesce(matches.category, 'Other')
                FROM candidates
                LEFT JOIN matches ON matches."Id" = candidates."Id"
                WHERE item."Id" = candidates."Id";
                """);

            // Conditions: the longest spelling the text contains, earliest on a tie; anything unreadable is kept.
            migrationBuilder.Sql("""
                WITH spellings(spelling, condition) AS (
                    VALUES
                    ('factory sealed', 'Factory sealed'),
                    ('new with tags', 'New with tags'),
                    ('new without tags', 'New without tags'),
                    ('new', 'New'),
                    ('open box', 'Open box'),
                    ('refurbished', 'Refurbished'),
                    ('like new', 'Like new'),
                    ('excellent', 'Excellent'),
                    ('very good', 'Very good'),
                    ('good', 'Good'),
                    ('fair', 'Fair'),
                    ('poor', 'Poor'),
                    ('for parts', 'For parts'),
                    ('nwt', 'New with tags'),
                    ('nwot', 'New without tags'),
                    ('sealed', 'Factory sealed'),
                    ('brand new', 'New'),
                    ('new in box', 'New'),
                    ('nib', 'New'),
                    ('uln', 'Like new'),
                    ('mint', 'Like new'),
                    ('near mint', 'Like new'),
                    ('nm', 'Like new'),
                    ('used nm', 'Like new'),
                    ('pre owned excellent', 'Excellent'),
                    ('lightly used', 'Excellent'),
                    ('light use', 'Excellent'),
                    ('lu', 'Excellent'),
                    ('gently used', 'Excellent'),
                    ('used lu', 'Excellent'),
                    ('pre owned good', 'Good'),
                    ('ug', 'Good'),
                    ('used', 'Good'),
                    ('pre owned', 'Good'),
                    ('moderate use', 'Good'),
                    ('moderate wear', 'Good'),
                    ('pre owned fair', 'Fair'),
                    ('uf', 'Fair'),
                    ('satisfactory', 'Fair'),
                    ('needs minor repair', 'Fair'),
                    ('needs alteration', 'Fair'),
                    ('heavy use', 'Poor'),
                    ('well worn', 'Poor'),
                    ('damaged', 'Poor'),
                    ('needs major repair', 'Poor'),
                    ('for parts or not working', 'For parts'),
                    ('not working', 'For parts')),
                candidates AS (
                    SELECT "Id", ' ' || btrim(regexp_replace(lower("Condition"), '[^a-z0-9]+', ' ', 'g')) || ' ' AS words
                    FROM "InventoryItems"
                    WHERE btrim(coalesce("Condition", '')) <> '' AND lower("Condition") NOT IN ('factory sealed', 'new with tags', 'new without tags', 'new', 'open box', 'refurbished', 'like new', 'excellent', 'very good', 'good', 'fair', 'poor', 'for parts')),
                matches AS (
                    SELECT DISTINCT ON (candidates."Id") candidates."Id", spellings.condition
                    FROM candidates
                    JOIN spellings ON candidates.words LIKE '% ' || spellings.spelling || ' %'
                    ORDER BY candidates."Id", length(spellings.spelling) DESC, position(' ' || spellings.spelling || ' ' IN candidates.words))
                UPDATE "InventoryItems" item
                SET "Condition" = matches.condition
                FROM matches
                WHERE item."Id" = matches."Id";
                """);

            // Sizes with no system: one size, a letter (with any US range dropped), or for clothing a plain US number.
            migrationBuilder.Sql("""
                WITH letters(written, size) AS (
                    VALUES
                    ('xxs', 'XXS'),
                    ('xs', 'XS'),
                    ('s', 'S'),
                    ('m', 'M'),
                    ('l', 'L'),
                    ('xl', 'XL'),
                    ('xxl', '2XL'),
                    ('2xl', '2XL'),
                    ('xxxl', '3XL'),
                    ('3xl', '3XL'),
                    ('4xl', '4XL'),
                    ('5xl', '5XL'),
                    ('small', 'S'),
                    ('medium', 'M'),
                    ('large', 'L'),
                    ('x-large', 'XL'),
                    ('xx-large', '2XL'),
                    ('extra small', 'XS'),
                    ('extra large', 'XL')),
                sized AS (
                    SELECT "Id", "Category", lower(btrim("SizeValue")) AS whole, lower(btrim(regexp_replace("SizeValue", '\s*(/|\().*$', ''))) AS size
                    FROM "InventoryItems"
                    WHERE "SizeSystem" IS NULL AND btrim(coalesce("SizeValue", '')) <> ''),
                read AS (
                    SELECT sized."Id",
                        CASE
                            WHEN sized.whole IN ('one size', 'os', 'o/s', 'osfa', 'one size fits all', 'one size fits most') AND sized."Category" IN ('Clothing', 'Baby') THEN 'One size'
                            WHEN letters.size IS NOT NULL AND sized."Category" = 'Clothing' THEN 'US letter'
                            WHEN sized.size ~ '^([0-9]|[1-5][0-9]|60)$' AND sized."Category" = 'Clothing' THEN 'US numeric'
                        END AS system,
                        CASE
                            WHEN sized.whole IN ('one size', 'os', 'o/s', 'osfa', 'one size fits all', 'one size fits most') THEN 'One size'
                            WHEN letters.size IS NOT NULL THEN letters.size
                            ELSE sized.size
                        END AS value
                    FROM sized
                    LEFT JOIN letters ON letters.written = sized.size)
                UPDATE "InventoryItems" item
                SET "SizeSystem" = read.system, "SizeValue" = read.value
                FROM read
                WHERE item."Id" = read."Id" AND read.system IS NOT NULL;
                """);
        }

        // What each marketplace wrote is still on its listing (PlatformListings); there is nothing to put back.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
