namespace ScoutHaul.Api.Helpers;

internal static class StringHelpers
{
    internal static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
