using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Orders;

/// <summary>
/// Folds customers that are demonstrably the same person into one, at startup, right after migrations.
///
/// Imports used to create a customer whenever a channel handle was unseen, so one buyer who shopped on
/// the storefront and later on a marketplace became two records with split history. The resolver no
/// longer does that, but rows written before the fix are still split, and nobody should have to find
/// and repair them by hand — the same reasoning as the shelf reconcile: deploys should not depend on
/// memory.
///
/// The only join is an exact, case-insensitive email match. Nothing is inferred from names or
/// addresses: silently combining two different buyers would corrupt order history far worse than
/// leaving them apart.
///
/// Idempotent: once folded there is nothing left to match, so every boot self-heals rather than only
/// the first one.
/// </summary>
public static class CustomerConvergenceBootstrap
{
    public static async Task ConvergeDuplicatesAsync(
        HaulScoutDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Find the duplicated addresses in the database rather than loading every customer to group
        // them here. Case-insensitive, because the storefront stores email lowercased while marketplace
        // imports historically stored it as the platform spelled it — so the unique index treats
        // "JANE@x.com" and "jane@x.com" as different rows when they are plainly one person.
        List<string> duplicatedEmails = await db.Customers
            .IgnoreQueryFilters()   // every organization's data; this runs before any request exists
            .Where(customer => customer.Email != null && customer.Email != "")
            .GroupBy(customer => customer.Email!.ToLower())
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);

        if (duplicatedEmails.Count == 0)
        {
            return;
        }

        List<Customer> candidates = await db.Customers
            .IgnoreQueryFilters()
            .Where(customer => customer.Email != null && duplicatedEmails.Contains(customer.Email.ToLower()))
            .ToListAsync(cancellationToken);

        int folded = 0;
        int skipped = 0;

        // Group by organization as well as address: two organizations may legitimately know the same
        // buyer, and their records must never be joined.
        foreach (IGrouping<(Guid OrganizationId, string Email), Customer> group in candidates
            .GroupBy(customer => (customer.OrganizationId, Email: customer.Email!.ToLowerInvariant()))
            .Where(candidate => candidate.Count() > 1))
        {
            List<Customer> records = [.. group];

            // ExternalSubject is the storefront sign-in link. Two different links on one address means
            // two real accounts, and folding them would destroy somebody's ability to log in — so leave
            // them for a person to look at rather than guessing.
            int signIns = records
                .Select(customer => customer.ExternalSubject)
                .Where(subject => !string.IsNullOrWhiteSpace(subject))
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (signIns > 1)
            {
                skipped++;
                logger.LogWarning(
                    "Customer convergence: {Count} records share an email but hold different sign-in links; leaving them alone.",
                    records.Count);
                continue;
            }

            Customer survivor = ChooseSurvivor(records);
            List<Customer> duplicates = records.Where(customer => customer != survivor).ToList();
            Guid[] duplicateIds = duplicates.Select(customer => customer.Id).ToArray();

            await AbsorbAsync(db, duplicateIds, survivor, cancellationToken);

            foreach (Customer duplicate in duplicates)
            {
                // Credit is money owed to a person, not to a row.
                survivor.StoreCreditBalance += duplicate.StoreCreditBalance;
                survivor.DisplayName ??= duplicate.DisplayName;
                survivor.Phone ??= duplicate.Phone;
                survivor.ExternalSubject ??= duplicate.ExternalSubject;
                survivor.AcceptsMarketing = survivor.AcceptsMarketing || duplicate.AcceptsMarketing;
                folded++;
            }

            // Normalize on the way out so one address can never split into two rows again.
            survivor.Email = survivor.Email?.ToLowerInvariant();
            db.Customers.RemoveRange(duplicates);
        }

        if (folded == 0 && skipped == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Customer convergence: folded {Folded} duplicate record(s) by email; {Skipped} group(s) left alone for holding different sign-in links.",
            folded, skipped);
    }

    /// <summary>
    /// The record everything else folds into. The sign-in link decides it: that is the row the customer
    /// actually authenticates against, and deleting it would lock them out. Only when no record holds
    /// one does age decide, because the oldest carries the longest history.
    /// </summary>
    private static Customer ChooseSurvivor(List<Customer> records) =>
        records
            .OrderByDescending(customer => !string.IsNullOrWhiteSpace(customer.ExternalSubject))
            .ThenBy(customer => customer.CreatedAt)
            .ThenBy(customer => customer.Id)
            .First();

    /// <summary>Repoints everything the duplicates own at the survivor.</summary>
    private static async Task AbsorbAsync(
        HaulScoutDbContext db,
        Guid[] duplicateIds,
        Customer survivor,
        CancellationToken cancellationToken)
    {
        foreach (Order order in await db.Orders.IgnoreQueryFilters()
            .Where(row => row.CustomerId != null && duplicateIds.Contains(row.CustomerId.Value))
            .ToListAsync(cancellationToken))
        {
            order.CustomerId = survivor.Id;
        }

        foreach (CustomerChannelIdentity identity in await db.CustomerChannelIdentities.IgnoreQueryFilters()
            .Where(row => duplicateIds.Contains(row.CustomerId)).ToListAsync(cancellationToken))
        {
            identity.CustomerId = survivor.Id;
        }

        foreach (WishlistItem wishlistItem in await db.WishlistItems.IgnoreQueryFilters()
            .Where(row => duplicateIds.Contains(row.CustomerId)).ToListAsync(cancellationToken))
        {
            wishlistItem.CustomerId = survivor.Id;
        }

        foreach (Review review in await db.Reviews.IgnoreQueryFilters()
            .Where(row => duplicateIds.Contains(row.CustomerId)).ToListAsync(cancellationToken))
        {
            review.CustomerId = survivor.Id;
        }

        bool survivorHasDefault = await db.CustomerAddresses.IgnoreQueryFilters()
            .AnyAsync(row => row.CustomerId == survivor.Id && row.IsDefault, cancellationToken);

        foreach (CustomerAddress address in await db.CustomerAddresses.IgnoreQueryFilters()
            .Where(row => duplicateIds.Contains(row.CustomerId))
            .OrderByDescending(row => row.IsDefault)
            .ToListAsync(cancellationToken))
        {
            address.CustomerId = survivor.Id;
            // Exactly one default survives: keep the survivor's, or promote the first moved one when it
            // had none, so a folded customer never ends up with no default address at all.
            if (survivorHasDefault)
            {
                address.IsDefault = false;
            }
            else if (address.IsDefault)
            {
                survivorHasDefault = true;
            }
        }
    }
}
