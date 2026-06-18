using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/product-identities")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ProductIdentitiesController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProductIdentityDto>> Create(UpsertProductIdentityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Product title is required.");
        }

        var product = new ProductIdentity
        {
            OrganizationId = currentUser.OrganizationId,
            Title = request.Title.Trim()
        };
        Apply(request, product);

        db.ProductIdentities.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = product.Id }, product.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductIdentityDto>> Update(Guid id, UpsertProductIdentityRequest request, CancellationToken cancellationToken)
    {
        var product = await db.ProductIdentities
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Product title is required.");
        }

        Apply(request, product);
        await db.SaveChangesAsync(cancellationToken);

        return product.ToDto();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductIdentityDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.ProductIdentities
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return product is null ? NotFound() : product.ToDto();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductIdentityDto>>> Search([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var query = db.ProductIdentities.Where(item => item.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Title, pattern)
                || (item.Brand != null && EF.Functions.ILike(item.Brand, pattern))
                || (item.Model != null && EF.Functions.ILike(item.Model, pattern))
                || (item.Upc != null && EF.Functions.ILike(item.Upc, pattern))
                || (item.SearchQuery != null && EF.Functions.ILike(item.SearchQuery, pattern)));
        }

        var products = await query
            .OrderByDescending(item => item.UpdatedAt)
            .Take(50)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return products;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var product = await db.ProductIdentities
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        db.ProductIdentities.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static void Apply(UpsertProductIdentityRequest request, ProductIdentity product)
    {
        product.Title = request.Title.Trim();
        product.Brand = TrimToNull(request.Brand);
        product.Model = TrimToNull(request.Model);
        product.Category = TrimToNull(request.Category);
        product.Upc = TrimToNull(request.Upc);
        product.Ean = TrimToNull(request.Ean);
        product.Isbn = TrimToNull(request.Isbn);
        product.Sku = TrimToNull(request.Sku);
        product.SearchQuery = TrimToNull(request.SearchQuery);
        product.Notes = TrimToNull(request.Notes);
        product.DataSource = "user"; // user-submitted = trusted, overrides external API data
    }

}
