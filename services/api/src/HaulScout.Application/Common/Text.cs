namespace HaulScout.Application.Common;

/// <summary>
/// Small text rules shared by the use cases. "Blank means absent" is a decision the whole application makes the same
/// way — an empty box in the UI and a field of spaces both mean "not set", never a stored empty string — so it is
/// written once here rather than re-declared privately by each service that needs it.
/// </summary>
public static class Text
{
    /// <summary>Null, empty and whitespace all collapse to null; anything else is trimmed.</summary>
    public static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
