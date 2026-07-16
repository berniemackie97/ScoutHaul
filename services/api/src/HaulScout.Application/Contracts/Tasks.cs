using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Resale work tasks.

public sealed record CreateTaskRequest(Guid? InventoryItemId, Guid? AssignedToUserId, string Title, string? Description, DateTimeOffset? DueAt);
public sealed record AssignTaskRequest(Guid? AssignedToUserId);
public sealed record UpdateTaskStatusRequest(WorkTaskStatus Status);
public sealed record ResaleTaskDto(Guid Id, Guid? InventoryItemId, Guid? AssignedToUserId, string Title, string? Description, WorkTaskStatus Status, DateTimeOffset? DueAt, DateTimeOffset CreatedAt);
