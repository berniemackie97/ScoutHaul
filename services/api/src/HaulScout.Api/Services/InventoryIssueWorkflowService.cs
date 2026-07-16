using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class InventoryIssueWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<AppResult<IReadOnlyList<InventoryIssueDto>>> ListAsync(Guid inventoryItemId, CancellationToken cancellationToken)
    {
        if (!await InventoryItemExistsAsync(inventoryItemId, cancellationToken))
        {
            return AppResult<IReadOnlyList<InventoryIssueDto>>.NotFound();
        }

        List<InventoryIssueDto> issues = await db.InventoryIssues
            .Where(item => item.InventoryItemId == inventoryItemId && item.OrganizationId == currentUser.OrganizationId)
            .OrderBy(item => item.IsResolved)
            .ThenBy(item => item.SortOrder)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return AppResult<IReadOnlyList<InventoryIssueDto>>.Ok(issues);
    }

    public async Task<AppResult<InventoryIssueDto>> CreateAsync(
        Guid inventoryItemId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        if (!await InventoryItemExistsAsync(inventoryItemId, cancellationToken))
        {
            return AppResult<InventoryIssueDto>.NotFound();
        }

        string? validation = await ValidateIssueRequestAsync(inventoryItemId, request, cancellationToken);
        if (validation is not null)
        {
            return AppResult<InventoryIssueDto>.Validation(validation);
        }

        InventoryIssue issue = new()
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = inventoryItemId,
            InventoryPhotoId = request.InventoryPhotoId,
            IssueType = request.IssueType.Trim(),
            Severity = request.Severity.Trim(),
            Note = TrimToNull(request.Note),
            SortOrder = request.SortOrder ?? await NextIssueSortOrderAsync(inventoryItemId, cancellationToken),
            IsResolved = request.IsResolved ?? false
        };

        db.InventoryIssues.Add(issue);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryIssueDto>.Ok(issue.ToDto());
    }

    public async Task<AppResult<InventoryIssueDto>> UpdateAsync(
        Guid inventoryItemId,
        Guid issueId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        InventoryIssue? issue = await db.InventoryIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.InventoryItemId == inventoryItemId && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (issue is null)
        {
            return AppResult<InventoryIssueDto>.NotFound();
        }

        string? validation = await ValidateIssueRequestAsync(inventoryItemId, request, cancellationToken);
        if (validation is not null)
        {
            return AppResult<InventoryIssueDto>.Validation(validation);
        }

        issue.InventoryPhotoId = request.InventoryPhotoId;
        issue.IssueType = request.IssueType.Trim();
        issue.Severity = request.Severity.Trim();
        issue.Note = TrimToNull(request.Note);
        issue.SortOrder = request.SortOrder ?? issue.SortOrder;
        issue.IsResolved = request.IsResolved ?? issue.IsResolved;

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryIssueDto>.Ok(issue.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid inventoryItemId, Guid issueId, CancellationToken cancellationToken)
    {
        InventoryIssue? issue = await db.InventoryIssues.SingleOrDefaultAsync(
            item => item.Id == issueId && item.InventoryItemId == inventoryItemId && item.OrganizationId == currentUser.OrganizationId,
            cancellationToken);
        if (issue is null)
        {
            return AppResult.NotFound();
        }

        db.InventoryIssues.Remove(issue);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private Task<bool> InventoryItemExistsAsync(Guid inventoryItemId, CancellationToken cancellationToken) =>
        db.InventoryItems.AnyAsync(item => item.Id == inventoryItemId && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

    private async Task<string?> ValidateIssueRequestAsync(
        Guid inventoryItemId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IssueType))
        {
            return "Issue type is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Severity))
        {
            return "Issue severity is required.";
        }

        if (request.InventoryPhotoId.HasValue)
        {
            bool photoExists = await db.InventoryPhotos.AnyAsync(
                item => item.Id == request.InventoryPhotoId.Value
                    && item.InventoryItemId == inventoryItemId
                    && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (!photoExists)
            {
                return "Issue photo does not belong to this inventory item.";
            }
        }

        return null;
    }

    private async Task<int> NextIssueSortOrderAsync(Guid inventoryItemId, CancellationToken cancellationToken)
    {
        int? max = await db.InventoryIssues
            .Where(item => item.InventoryItemId == inventoryItemId && item.OrganizationId == currentUser.OrganizationId)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken);
        return (max ?? -1) + 1;
    }
}
