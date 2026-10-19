using System.Text.Json;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Appraisal;

public sealed record ListingCopy(string Title, string Description);

/// <summary>Writes listing words from item details; pricing is never its job.</summary>
public interface IListingCopyWriter
{
    bool IsConfigured { get; }
    Task<ListingCopy?> WriteAsync(string details, AppraisalDto? appraisal, CancellationToken cancellationToken);
}

/// <summary>A ready-to-post listing: words from the copy writer, price from the same appraisal as everywhere else.</summary>
public sealed class ListingGenerationService(AppraisalService appraisals, IListingCopyWriter writer)
{
    public bool IsConfigured => writer.IsConfigured;

    public async Task<AppResult<GeneratedListingDto>> GenerateAsync(GenerateListingRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return AppResult<GeneratedListingDto>.Validation("Give the item a title first.", "title_required");
        }

        AppraisalDto? appraisal = (await appraisals.AppraiseAsync(ToAppraisal(request), cancellationToken)).Value;
        ListingCopy? copy = await writer.WriteAsync(Details(request), appraisal, cancellationToken);
        if (copy is null)
        {
            return AppResult<GeneratedListingDto>.NotFound("The listing writer did not answer. Try again in a moment.", "generation_failed");
        }

        PriceDto? price = appraisal?.Price;
        return AppResult<GeneratedListingDto>.Ok(new GeneratedListingDto(
            copy.Title,
            copy.Description,
            price?.List,
            price?.QuickSale,
            price?.High,
            price is null ? null : string.Join(' ', price.Reasons.Take(3)),
            (appraisal?.Comps ?? [])
                .Where(comp => comp.Included)
                .Take(8)
                .Select(comp => new ResaleComp(
                    comp.Price,
                    comp.Kind == CompKind.Asking ? "active" : "sold",
                    comp.Condition,
                    comp.Source,
                    comp.Url,
                    comp.Title,
                    comp.Date))
                .ToArray(),
            price?.Basis,
            price?.Confidence));
    }

    private static AppraiseRequest ToAppraisal(GenerateListingRequest request) => new(
        request.Title.Trim(),
        request.Brand,
        request.Model,
        request.Category,
        request.Color,
        request.Condition,
        Identifiers: Identifiers(request),
        Certainty: IdentificationCertainty.High);

    // Size and structured attributes (card number, edition, language) pin the variant for matching.
    private static IReadOnlyList<string> Identifiers(GenerateListingRequest request)
    {
        List<string> identifiers = [];
        string size = string.Join(' ', new[] { request.SizeSystem, request.SizeValue }.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (size.Length > 0)
        {
            identifiers.Add(size);
        }

        if (!string.IsNullOrWhiteSpace(request.AttributesJson))
        {
            try
            {
                using JsonDocument attributes = JsonDocument.Parse(request.AttributesJson);
                if (attributes.RootElement.ValueKind == JsonValueKind.Object)
                {
                    identifiers.AddRange(attributes.RootElement.EnumerateObject()
                        .Where(property => property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        .Select(property => property.Value.ToString().Trim())
                        .Where(value => value.Length is > 0 and <= 80));
                }
            }
            catch (JsonException)
            {
                // Unreadable attributes add nothing; the title still identifies the item.
            }
        }

        return identifiers.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static string Details(GenerateListingRequest request)
    {
        List<string> lines = [request.Title.Trim()];
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value.Trim()}");
        }

        Add("Brand", request.Brand);
        Add("Model", request.Model);
        Add("Category", request.Category);
        Add("Condition", request.Condition);
        Add("Color", request.Color);
        Add("Size", string.Join(' ', new[] { request.SizeSystem, request.SizeValue }.Where(part => !string.IsNullOrWhiteSpace(part))));
        Add("Structured attributes JSON", request.AttributesJson);
        Add("Notes", request.Notes);
        return string.Join('\n', lines);
    }
}
