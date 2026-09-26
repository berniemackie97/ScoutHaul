using System.Linq.Expressions;
using HaulScout.Domain.Entities;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>What typing into any inventory search matches, so every list and picker finds the same items.</summary>
internal static class InventorySearch
{
    public static Expression<Func<InventoryItem, bool>> Matching(string search)
    {
        string term = search.ToLower();
        return item =>
            item.Title.ToLower().Contains(term)
            || (item.Brand != null && item.Brand.ToLower().Contains(term))
            || (item.Model != null && item.Model.ToLower().Contains(term))
            || (item.Category != null && item.Category.ToLower().Contains(term))
            || (item.Barcode != null && item.Barcode.ToLower().Contains(term))
            || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(term));
    }
}
