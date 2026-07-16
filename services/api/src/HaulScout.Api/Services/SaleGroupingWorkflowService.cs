using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

/// <summary>
/// Combining already-recorded sales into one marketplace order, and splitting them back out. Each sale keeps its own
/// allocated price, fees, and net — this only owns the order-header relationship between them.
/// </summary>
public sealed class SaleGroupingWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter)
{
    /// <summary>Combine several sales under one order — the way eBay groups a multi-item order (one package, one
    /// tracking). Selecting sales already in a group merges them.</summary>
    public async Task<AppResult<SaleGroupResultDto>> GroupAsync(GroupSalesRequest request, CancellationToken cancellationToken)
    {
        Guid[] ids = (request.SaleIds ?? []).Distinct().ToArray();
        if (ids.Length < 2)
        {
            return AppResult<SaleGroupResultDto>.Validation("Select at least two sales to combine into one order.");
        }

        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Order)
            .Where(sale => ids.Contains(sale.Id) && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (sales.Count != ids.Length)
        {
            return AppResult<SaleGroupResultDto>.NotFound("One or more sales were not found.");
        }

        string? reference = TrimToNull(request.OrderReference)
            ?? sales.Select(sale => sale.Order?.ExternalOrderId ?? sale.GroupOrderReference).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        List<string> selectedChannels = sales.Select(sale => sale.Platform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selectedChannels.Count > 1)
        {
            return AppResult<SaleGroupResultDto>.Validation("Sales from different channels cannot be combined into one marketplace order.");
        }
        string channel = selectedChannels[0];

        Order? order = sales.Select(sale => sale.Order).FirstOrDefault(existing => existing is not null);
        if (order is null)
        {
            DateTimeOffset orderedAt = sales.Min(sale => sale.SoldAt);
            order = await orderWriter.GetOrCreateAsync(
                channel,
                reference,
                externalOrderUrl: null,
                buyerReference: null,
                buyerName: null,
                orderedAt,
                channelAccountId: null,
                itemsSubtotal: sales.Sum(sale => sale.SoldPrice),
                shippingTotal: sales.Sum(sale => sale.ShippingCharged),
                taxTotal: sales.Sum(sale => sale.SalesTaxCollected),
                cancellationToken);
        }
        Guid groupId = order.Id;

        foreach (Sale sale in sales)
        {
            sale.SaleGroupId = groupId;
            sale.GroupOrderReference = reference;
            sale.OrderId = order.Id;
            sale.Order = order;
        }

        await orderWriter.SyncSalesOrderAsync(order, sales, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        List<SaleDto> dtos = sales.OrderByDescending(sale => sale.SoldAt).Select(sale => sale.ToDto()).ToList();
        return AppResult<SaleGroupResultDto>.Ok(new SaleGroupResultDto(groupId, reference, dtos));
    }

    /// <summary>Split selected sale lines out of a combined order. Every sale still keeps a real Order header so
    /// Orders/Customers remain complete; only the shared-order relationship is split.</summary>
    public async Task<AppResult<IReadOnlyList<SaleDto>>> UngroupAsync(UngroupSalesRequest request, CancellationToken cancellationToken)
    {
        Guid[] ids = (request.SaleIds ?? []).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return AppResult<IReadOnlyList<SaleDto>>.Validation("Select at least one sale to remove from its order.");
        }

        List<Sale> selected = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Order)
            .Where(sale => ids.Contains(sale.Id) && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (selected.Count == 0)
        {
            return AppResult<IReadOnlyList<SaleDto>>.NotFound();
        }

        List<Guid> affectedOrders = selected.Where(sale => sale.OrderId.HasValue).Select(sale => sale.OrderId!.Value).Distinct().ToList();
        List<Sale> orderSales = affectedOrders.Count == 0
            ? []
            : await db.Sales
                .Include(sale => sale.InventoryItem)
                .Include(sale => sale.Order)
                .Where(sale => sale.OrderId != null && affectedOrders.Contains(sale.OrderId.Value) && sale.OrganizationId == currentUser.OrganizationId)
                .ToListAsync(cancellationToken);

        HashSet<Guid> selectedIds = ids.ToHashSet();
        foreach (Guid orderId in affectedOrders)
        {
            List<Sale> members = orderSales.Where(sale => sale.OrderId == orderId).OrderBy(sale => sale.SoldAt).ToList();
            if (members.Count <= 1)
            {
                continue;
            }

            Order? originalOrder = members.Select(sale => sale.Order).FirstOrDefault(order => order is not null);
            if (originalOrder is null)
            {
                continue;
            }

            List<Sale> selectedMembers = members.Where(sale => selectedIds.Contains(sale.Id)).ToList();
            if (selectedMembers.Count == 0)
            {
                continue;
            }

            // If the operator selects the whole order, keep one sale on the original header and split the rest.
            // This avoids creating an orphaned empty order while still dissolving the bundle presentation.
            List<Sale> toSplit = selectedMembers.Count == members.Count ? selectedMembers.Skip(1).ToList() : selectedMembers;
            foreach (Sale sale in toSplit)
            {
                sale.SaleGroupId = null;
                sale.GroupOrderReference = null;
                Order splitOrder = await orderWriter.GetOrCreateAsync(
                    sale.Platform,
                    externalOrderId: null,
                    externalOrderUrl: null,
                    buyerReference: null,
                    buyerName: null,
                    sale.SoldAt,
                    channelAccountId: null,
                    itemsSubtotal: sale.SoldPrice,
                    shippingTotal: sale.ShippingCharged,
                    taxTotal: sale.SalesTaxCollected,
                    cancellationToken);
                sale.OrderId = splitOrder.Id;
                sale.Order = splitOrder;
                await orderWriter.SyncSalesOrderAsync(splitOrder, [sale], cancellationToken);
            }

            List<Sale> remaining = members.Where(sale => !toSplit.Any(split => split.Id == sale.Id)).ToList();
            if (remaining.Count == 1)
            {
                remaining[0].SaleGroupId = null;
                remaining[0].GroupOrderReference = null;
            }
            else
            {
                foreach (Sale member in remaining)
                {
                    member.SaleGroupId = originalOrder.Id;
                    member.GroupOrderReference = originalOrder.ExternalOrderId ?? originalOrder.OrderNumber;
                }
            }

            await orderWriter.SyncSalesOrderAsync(originalOrder, remaining, cancellationToken, toSplit.Select(sale => sale.Id).ToList());
        }

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<IReadOnlyList<SaleDto>>.Ok(selected.Select(sale => sale.ToDto()).ToList());
    }
}
