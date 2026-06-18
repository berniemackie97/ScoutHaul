namespace HaulScout.Application.Services;

/// <summary>
/// Thrown when the vision provider could not be reached or refused the request (quota/rate/outage)
/// — as opposed to running fine but not recognizing anything (that returns a null product, not this).
/// <see cref="Code"/> is a stable machine code the client maps to a specific message + CTA:
/// quota_exhausted | rate_limited | upstream_unavailable | upstream_error.
/// </summary>
public sealed class VisionUnavailableException(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
}
