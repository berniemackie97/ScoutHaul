using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Storefront;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Storefront;

public sealed class EfStorefrontCustomerStore(HaulScoutDbContext db) : IStorefrontCustomerStore
{
    private const string StorefrontChannel = "Storefront";

    public async Task<Customer> GetOrCreateAsync(
        Guid organizationId,
        StorefrontCustomerIdentity identity,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken)
    {
        Customer? customer = await db.Customers
            .Include(item => item.ChannelIdentities)
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.ExternalSubject == identity.ExternalSubject,
                cancellationToken);

        customer ??= await db.CustomerChannelIdentities
            .Include(item => item.Customer)
                .ThenInclude(item => item!.ChannelIdentities)
            .Where(item =>
                item.OrganizationId == organizationId
                && item.Channel == StorefrontChannel
                && item.ExternalUserId == identity.ExternalSubject)
            .Select(item => item.Customer)
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null && identity.Email is string email)
        {
            customer = await db.Customers
                .Include(item => item.ChannelIdentities)
                .SingleOrDefaultAsync(
                    item => item.OrganizationId == organizationId && item.Email == email.ToLowerInvariant(),
                    cancellationToken);
        }

        if (customer is null)
        {
            customer = new Customer
            {
                OrganizationId = organizationId,
                ExternalSubject = identity.ExternalSubject,
                Email = identity.Email?.ToLowerInvariant(),
                DisplayName = identity.DisplayName,
                Phone = identity.Phone,
            };
            db.Customers.Add(customer);
        }
        else
        {
            customer.ExternalSubject ??= identity.ExternalSubject;
            customer.Email ??= identity.Email?.ToLowerInvariant();
            customer.DisplayName = identity.DisplayName ?? customer.DisplayName;
            customer.Phone = identity.Phone ?? customer.Phone;
        }

        CustomerChannelIdentity? channelIdentity = customer.ChannelIdentities.SingleOrDefault(item =>
            item.OrganizationId == organizationId
            && item.Channel == StorefrontChannel
            && item.ExternalUserId == identity.ExternalSubject);

        if (channelIdentity is null)
        {
            customer.ChannelIdentities.Add(new CustomerChannelIdentity
            {
                OrganizationId = organizationId,
                CustomerId = customer.Id,
                Channel = StorefrontChannel,
                ExternalUserId = identity.ExternalSubject,
                DisplayName = identity.DisplayName,
                Email = identity.Email?.ToLowerInvariant(),
                FirstSeenAt = seenAt,
                LastSeenAt = seenAt,
            });
        }
        else
        {
            channelIdentity.DisplayName = identity.DisplayName ?? channelIdentity.DisplayName;
            channelIdentity.Email = identity.Email?.ToLowerInvariant() ?? channelIdentity.Email;
            channelIdentity.LastSeenAt = seenAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return customer;
    }
}
