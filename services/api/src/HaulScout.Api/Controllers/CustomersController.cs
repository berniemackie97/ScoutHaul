using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/customers")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrders)]
public sealed class CustomersController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>Buyers from every channel — a storefront shopper (email) and an eBay/Depop buyer (username) are
    /// both customers here. Pass <paramref name="channel"/> to narrow to one platform.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerSummaryDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? channel,
        CancellationToken cancellationToken)
    {
        var query = db.Customers
            .Include(customer => customer.Orders)
            .Include(customer => customer.ChannelIdentities)
            .Where(customer => customer.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(channel))
        {
            string wanted = channel.Trim();
            query = query.Where(customer => customer.ChannelIdentities.Any(identity => identity.Channel == wanted));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(customer =>
                (customer.Email != null && EF.Functions.ILike(customer.Email, $"%{term}%"))
                || (customer.DisplayName != null && EF.Functions.ILike(customer.DisplayName, $"%{term}%"))
                || customer.ChannelIdentities.Any(identity => EF.Functions.ILike(identity.ExternalUserId, $"%{term}%")));
        }

        var customers = await query
            .OrderByDescending(customer => customer.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return customers.Select(customer => customer.ToSummaryDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers
            .Include(item => item.Addresses)
            .Include(item => item.ChannelIdentities)
            .Include(item => item.Orders)
                .ThenInclude(order => order.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return customer is null ? NotFound() : customer.ToDetailDto();
    }
}
