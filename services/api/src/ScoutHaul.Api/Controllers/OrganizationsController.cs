using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Organization name is required.");
        }

        var user = await db.Users.FindAsync([currentUser.UserId], cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                Id = currentUser.UserId,
                Email = $"owner-{currentUser.UserId:N}@scouthaul.local",
                DisplayName = "Owner"
            };
            db.Users.Add(user);
        }

        var organization = new Organization { Name = request.Name.Trim() };
        organization.Members.Add(new OrganizationMember
        {
            UserId = user.Id,
            Role = Domain.OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        });

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = organization.Id }, organization.ToDto());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.FindAsync([id], cancellationToken);
        return organization is null ? NotFound() : organization.ToDto();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(Guid id, CancellationToken cancellationToken)
    {
        var members = await db.OrganizationMembers
            .Include(member => member.User)
            .Where(member => member.OrganizationId == id)
            .OrderBy(member => member.User!.DisplayName)
            .Select(member => member.ToDto())
            .ToListAsync(cancellationToken);

        return members;
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(Guid id, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest("Email and display name are required.");
        }

        var organizationExists = await db.Organizations.AnyAsync(organization => organization.Id == id, cancellationToken);
        if (!organizationExists)
        {
            return NotFound();
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(existing => existing.Email == email, cancellationToken);
        if (user is null)
        {
            user = new AppUser { Email = email, DisplayName = request.DisplayName.Trim() };
            db.Users.Add(user);
        }

        var existingMember = await db.OrganizationMembers
            .SingleOrDefaultAsync(member => member.OrganizationId == id && member.UserId == user.Id, cancellationToken);

        if (existingMember is not null)
        {
            return Conflict("User is already a member of this organization.");
        }

        var member = new OrganizationMember
        {
            OrganizationId = id,
            UserId = user.Id,
            Role = request.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        db.OrganizationMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        member.User = user;

        return CreatedAtAction(nameof(ListMembers), new { id }, member.ToDto());
    }
}
