using HaulScout.Domain.Entities;

namespace HaulScout.Application.Contracts;

public enum WorkStepState
{
    Done,
    Now,
    Later,
}

/// <summary>One step of the thread a task belongs to: sold, paid, packed, shipped, delivered.</summary>
public sealed record WorkStepDto(string Label, DateTimeOffset? At, WorkStepState State);

public sealed record WorkTaskDto(
    string Key,
    string Lane,
    string Kind,
    string Title,
    string? Detail,
    // Why it matters, in a sentence.
    string Why,
    string ActionLabel,
    string ActionHref,
    DateTimeOffset? Since,
    DateTimeOffset? DueBy,
    bool IsLate,
    IReadOnlyList<WorkStepDto> Steps,
    // The task stands for this many records of the same kind (a queue rather than one thing).
    int Count = 1);

public sealed record WorkLaneDto(string Key, string Label, string Hint, IReadOnlyList<WorkTaskDto> Tasks);

public sealed record WorkWaitDto(string Key, WorkWait Wait, DateTimeOffset? Until, string? Note, string? Title);

public sealed record WorkInboxDto(
    IReadOnlyList<WorkLaneDto> Lanes,
    IReadOnlyList<WorkWaitDto> Waiting,
    // In transit with a carrier: waiting, not work.
    int InTransit,
    int OpenCount,
    int LateCount,
    DateTimeOffset GeneratedAt);

public sealed record SetAsideWorkTaskRequest(string TaskKey, WorkWait Wait, DateTimeOffset? Until = null, string? Note = null, string? Title = null);
