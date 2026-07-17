using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using static HaulScout.Application.Common.Text;

namespace HaulScout.Application.Inventory;

/// <summary>
/// Recording what is wrong with an item. An issue can be pinned to the photo that shows the defect, which is the one
/// rule worth guarding: the photo must belong to the same item, or the issue would point at a picture of something else.
/// </summary>
public sealed class InventoryIssueWorkflowService(
    IInventoryStore inventory,
    IInventoryIssueStore issues,
    ICurrentUserContext currentUser)
{
    public async Task<AppResult<IReadOnlyList<InventoryIssueDto>>> ListAsync(Guid inventoryItemId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        if (!await inventory.ExistsAsync(organizationId, inventoryItemId, cancellationToken))
        {
            return AppResult<IReadOnlyList<InventoryIssueDto>>.NotFound();
        }

        IReadOnlyList<InventoryIssue> records = await issues.ListAsync(organizationId, inventoryItemId, cancellationToken);
        return AppResult<IReadOnlyList<InventoryIssueDto>>.Ok(records.Select(item => item.ToDto()).ToArray());
    }

    public async Task<AppResult<InventoryIssueDto>> CreateAsync(
        Guid inventoryItemId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        if (!await inventory.ExistsAsync(organizationId, inventoryItemId, cancellationToken))
        {
            return AppResult<InventoryIssueDto>.NotFound();
        }

        if (await ValidateAsync(inventoryItemId, request, cancellationToken) is string validation)
        {
            return AppResult<InventoryIssueDto>.Validation(validation);
        }

        InventoryIssue issue = new()
        {
            OrganizationId = organizationId,
            InventoryItemId = inventoryItemId,
            InventoryPhotoId = request.InventoryPhotoId,
            IssueType = request.IssueType.Trim(),
            Severity = request.Severity.Trim(),
            Note = TrimToNull(request.Note),
            SortOrder = request.SortOrder ?? await issues.NextSortOrderAsync(organizationId, inventoryItemId, cancellationToken),
            IsResolved = request.IsResolved ?? false,
        };

        await issues.AddAsync(issue, cancellationToken);
        await issues.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryIssueDto>.Ok(issue.ToDto());
    }

    public async Task<AppResult<InventoryIssueDto>> UpdateAsync(
        Guid inventoryItemId,
        Guid issueId,
        UpsertInventoryIssueRequest request,
        CancellationToken cancellationToken)
    {
        InventoryIssue? issue = await issues.FindAsync(currentUser.OrganizationId, inventoryItemId, issueId, cancellationToken);
        if (issue is null)
        {
            return AppResult<InventoryIssueDto>.NotFound();
        }

        if (await ValidateAsync(inventoryItemId, request, cancellationToken) is string validation)
        {
            return AppResult<InventoryIssueDto>.Validation(validation);
        }

        issue.InventoryPhotoId = request.InventoryPhotoId;
        issue.IssueType = request.IssueType.Trim();
        issue.Severity = request.Severity.Trim();
        issue.Note = TrimToNull(request.Note);
        issue.SortOrder = request.SortOrder ?? issue.SortOrder;
        issue.IsResolved = request.IsResolved ?? issue.IsResolved;

        await issues.SaveChangesAsync(cancellationToken);
        return AppResult<InventoryIssueDto>.Ok(issue.ToDto());
    }

    public async Task<AppResult> DeleteAsync(Guid inventoryItemId, Guid issueId, CancellationToken cancellationToken)
    {
        InventoryIssue? issue = await issues.FindAsync(currentUser.OrganizationId, inventoryItemId, issueId, cancellationToken);
        if (issue is null)
        {
            return AppResult.NotFound();
        }

        issues.Remove(issue);
        await issues.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    private async Task<string?> ValidateAsync(
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

        if (request.InventoryPhotoId is Guid photoId
            && !await issues.PhotoBelongsToItemAsync(currentUser.OrganizationId, inventoryItemId, photoId, cancellationToken))
        {
            return "Issue photo does not belong to this inventory item.";
        }

        return null;
    }
}
