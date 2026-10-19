namespace HaulScout.Application.Appraisal;

public static class CompHonesty
{
    /// <summary>Older app builds labelled a model's figures "AI …"; those are estimates, never sales or listings.</summary>
    public static bool IsModelSource(string? source)
    {
        string trimmed = source?.Trim() ?? string.Empty;
        return trimmed.Equals("AI", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("AI ", StringComparison.OrdinalIgnoreCase);
    }
}
