using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>What the business already knows about a barcode: a saved product, else an item it has stocked.</summary>
internal sealed class EfProductCatalogStore(HaulScoutDbContext db) : IProductCatalogStore
{
    public async Task<CatalogProduct?> FindByBarcodeAsync(Guid organizationId, string barcode, CancellationToken cancellationToken)
    {
        CatalogProduct? product = await db.ProductIdentities
            .AsNoTracking()
            .Where(product => product.OrganizationId == organizationId
                && (product.Upc == barcode || product.Ean == barcode || product.Isbn == barcode))
            .OrderByDescending(product => product.DataSource == "user")
            .ThenByDescending(product => product.UpdatedAt)
            .Select(product => new CatalogProduct(
                (Guid?)product.Id, product.Title, product.Brand, product.Model, product.Category, product.DataSource == "user"))
            .FirstOrDefaultAsync(cancellationToken);
        if (product is not null)
        {
            return product;
        }

        return await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Barcode == barcode)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new CatalogProduct(
                item.ProductIdentityId, item.Title, item.Brand, item.Model, item.Category, true))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
