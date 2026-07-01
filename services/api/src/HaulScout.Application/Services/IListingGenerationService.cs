using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>
/// Generates a ready-to-post marketplace listing from an item's details: an optimized title, an
/// honest description, and a competitive suggested list price backed by real sold/active comps.
/// </summary>
public interface IListingGenerationService
{
    bool IsConfigured { get; }

    Task<GeneratedListingDto?> GenerateAsync(GenerateListingRequest request, CancellationToken cancellationToken);
}
