using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/tasks")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageTasks)]
public sealed class TasksController(ScoutHaulDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ResaleTaskDto>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest("Task title is required.");
        }

        var task = new ResaleTask
        {
            OrganizationId = currentUser.OrganizationId,
            InventoryItemId = request.InventoryItemId,
            AssignedToUserId = request.AssignedToUserId,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            DueAt = request.DueAt
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(List), task.ToDto());
    }

    [HttpPatch("{id:guid}/assignee")]
    public async Task<ActionResult<ResaleTaskDto>> Assign(Guid id, AssignTaskRequest request, CancellationToken cancellationToken)
    {
        var task = await FindTask(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        task.AssignedToUserId = request.AssignedToUserId;
        await db.SaveChangesAsync(cancellationToken);
        return task.ToDto();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ResaleTaskDto>> UpdateStatus(Guid id, UpdateTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var task = await FindTask(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        task.Status = request.Status;
        await db.SaveChangesAsync(cancellationToken);
        return task.ToDto();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResaleTaskDto>>> List(CancellationToken cancellationToken)
    {
        var tasks = await db.Tasks
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .OrderBy(item => item.Status)
            .ThenBy(item => item.DueAt)
            .ThenByDescending(item => item.CreatedAt)
            .Take(200)
            .Select(item => item.ToDto())
            .ToListAsync(cancellationToken);

        return tasks;
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var task = await FindTask(id, cancellationToken);
        if (task is null)
        {
            return NotFound();
        }

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Task<ResaleTask?> FindTask(Guid id, CancellationToken cancellationToken) =>
        db.Tasks.SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
