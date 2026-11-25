using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

/// <summary>What a task set aside is waiting on. Every wait is named, so nothing leaves the inbox without a reason.</summary>
public enum WorkWait
{
    /// <summary>Set aside by a person until a date.</summary>
    Snoozed,
    Buyer,
    Carrier,
    Marketplace,
    Payout,
}

/// <summary>
/// A task in the work inbox set aside: snoozed to a date or waiting on someone. Keyed by the task's stable key
/// ("order:{id}:pack"), so when the work behind it moves on, the snooze no longer matches anything and drops away.
/// </summary>
public sealed class WorkSnooze : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public required string TaskKey { get; set; }
    public WorkWait Wait { get; set; }

    /// <summary>When it comes back to its lane, marked late. Required for a snooze; optional for a named wait.</summary>
    public DateTimeOffset? Until { get; set; }
    public string? Note { get; set; }

    /// <summary>The task's title when it was set aside, so the waiting list can say what it is.</summary>
    public string? Title { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
