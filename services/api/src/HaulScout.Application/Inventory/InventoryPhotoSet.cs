using System.Text.Json;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>
/// An item's photos as one thing: their order, which is the hero, and the addresses the rest of HaulScout reads
/// them by. Hero and order are properties of the set rather than of any one photo, so every path that changes an
/// item's photos — an upload, a deletion, a listing's photos arriving — arranges the set through here.
/// </summary>
public static class InventoryPhotoSet
{
    public static string OriginalUrl(Guid inventoryItemId, Guid photoId) => $"/api/inventory/{inventoryItemId}/photos/{photoId}/original";

    public static string ThumbnailUrl(Guid inventoryItemId, Guid photoId) => $"/api/inventory/{inventoryItemId}/photos/{photoId}/thumbnail";

    /// <summary>The photos in the order they are shown: the hero, then the rest by their order, then by age.</summary>
    public static IReadOnlyList<InventoryPhoto> Ordered(InventoryItem item) =>
        item.Photos
            .OrderByDescending(photo => photo.IsHero)
            .ThenBy(photo => photo.SortOrder)
            .ThenBy(photo => photo.CreatedAt)
            .ToArray();

    public static List<string> ParseReferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Serialize(IEnumerable<string> references) => JsonSerializer.Serialize(references);

    /// <summary>
    /// Puts an item's photos in this order, makes the first of them the hero, and rewrites the addresses the item
    /// carries so they say the same thing.
    /// </summary>
    public static void Arrange(InventoryItem item, IReadOnlyList<InventoryPhoto> ordered)
    {
        for (int index = 0; index < ordered.Count; index++)
        {
            ordered[index].SortOrder = index;
            ordered[index].IsHero = index == 0;
        }

        item.PhotoLocalReferencesJson = ordered.Count == 0
            ? null
            : Serialize(ordered.Select(photo => OriginalUrl(item.Id, photo.Id)));
    }
}
