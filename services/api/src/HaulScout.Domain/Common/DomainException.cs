namespace HaulScout.Domain.Common;

/// <summary>
/// A domain rule was violated — the caller asked an aggregate to do something its current state forbids (refunding a
/// return before the item is back, issuing a second label). Thrown by the aggregate rather than checked by the
/// caller, so the rule cannot be forgotten at one of several call sites.
/// </summary>
public sealed class DomainException(string message) : Exception(message);
