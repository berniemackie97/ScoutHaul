using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Domain;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Appraisal;

internal sealed class EfChannelEconomicsStore(HaulScoutDbContext db) : IChannelEconomicsStore
{
    private static readonly SaleStatus[] Realised =
        [SaleStatus.Recorded, SaleStatus.Packed, SaleStatus.Shipped, SaleStatus.Delivered, SaleStatus.PartiallyRefunded];

    public async Task<IReadOnlyList<ChannelAccountRef>> ListActiveChannelsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.ChannelAccounts
            .AsNoTracking()
            .Where(account => account.OrganizationId == organizationId && account.IsActive)
            .OrderBy(account => account.Channel)
            .Select(account => new ChannelAccountRef(account.Channel, account.DefaultFeeProfileId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, ChannelSalesHistory>> SalesHistoryAsync(
        Guid organizationId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var totals = await db.Sales
            .AsNoTracking()
            .Where(sale => sale.OrganizationId == organizationId
                && sale.SoldAt >= since
                && sale.SoldPrice > 0
                && Realised.Contains(sale.Status))
            .GroupBy(sale => sale.Platform)
            .Select(group => new
            {
                Platform = group.Key,
                Sales = group.Count(),
                SoldTotal = group.Sum(sale => sale.SoldPrice),
                FeeTotal = group.Sum(sale => sale.PlatformFees + sale.PaymentFees),
                ShippingSamples = group.Count(sale => sale.ShippingCost > 0 || sale.ShippingCharged > 0),
                NetShippingTotal = group.Sum(sale => sale.ShippingCost > 0 || sale.ShippingCharged > 0
                    ? sale.ShippingCost - sale.ShippingCharged
                    : 0),
                SuppliesTotal = group.Sum(sale => sale.SuppliesCost),
            })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(
            total => total.Platform,
            total => new ChannelSalesHistory(
                total.Sales, total.SoldTotal, total.FeeTotal, total.ShippingSamples, total.NetShippingTotal, total.SuppliesTotal),
            StringComparer.OrdinalIgnoreCase);
    }
}
