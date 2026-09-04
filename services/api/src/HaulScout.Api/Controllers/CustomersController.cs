using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/customers")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrders)]
public sealed class CustomersController(HaulScoutDbContext db, ICurrentUserContext currentUser) : HaulScoutControllerBase
{
    /// <summary>Buyers from every channel — a storefront shopper (email) and an eBay/Depop buyer (username) are
    /// both customers here. Pass <paramref name="channel"/> to narrow to one platform.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerSummaryDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? channel,
        CancellationToken cancellationToken)
    {
        IQueryable<Customer> query = db.Customers
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

        List<Customer> customers = await query
            .OrderByDescending(customer => customer.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return customers.Select(customer => customer.ToSummaryDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Customer? customer = await db.Customers
            .Include(item => item.Addresses)
            .Include(item => item.ChannelIdentities)
            .Include(item => item.Orders)
                .ThenInclude(order => order.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return customer is null ? NotFound() : customer.ToDetailDto();
    }

    /// <summary>
    /// Corrects a customer's own details. Customers are created by checkout and by imports, so the
    /// details arrive as the platform spelled them and were previously impossible to fix.
    ///
    /// Store credit and the auth link are not editable here: credit is money and moves only through a
    /// credit entry, and the auth link is what identifies the account rather than describes it.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Update(
        Guid id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        Customer? customer = await db.Customers
            .Include(item => item.Addresses)
            .Include(item => item.ChannelIdentities)
            .Include(item => item.Orders)
                .ThenInclude(order => order.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (customer is null)
        {
            return Missing();
        }

        string? email = Trim(request.Email);
        if (email is not null && !MailAddress.TryCreate(email, out _))
        {
            return Invalid("Enter a valid email address.");
        }

        // Email is unique per organization. Checking here turns a would-be 500 from the unique index
        // into an explanation the operator can act on — usually "these are the same person", which is
        // a merge, not an edit.
        if (email is not null
            && !string.Equals(email, customer.Email, StringComparison.OrdinalIgnoreCase)
            && await db.Customers.AnyAsync(
                other => other.OrganizationId == currentUser.OrganizationId
                    && other.Id != customer.Id
                    && other.Email != null
                    && other.Email.ToLower() == email.ToLower(),
                cancellationToken))
        {
            return Blocked("Another customer already uses that email address.");
        }

        customer.DisplayName = Trim(request.DisplayName);
        customer.Email = email;
        customer.Phone = Trim(request.Phone);
        customer.AcceptsMarketing = request.AcceptsMarketing;

        await db.SaveChangesAsync(cancellationToken);
        return customer.ToDetailDto();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
