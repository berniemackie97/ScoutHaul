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

    /// <summary>Cut to fit a column, marking the cut.</summary>
    public static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>Free text as a column can hold it: blank is absent, and anything longer is cut.</summary>
    public static string? Fit(string? value, int max) => Clip(TrimToNull(value), max);
}
