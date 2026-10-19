using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Application.Appraisal;

/// <summary>Identifies an item, gathers its comps from every source at once, and prices it.</summary>
public sealed partial class AppraisalService(
    IProductCatalogStore catalog,
    IBarcodeLookupService barcodes,
    IVisionIdentificationService vision,
    IEnumerable<ICompSource> sources,
    IAppraisalHistoryStore history,
    ICurrentUserContext currentUser,
    TimeProvider clock,
    ILogger<AppraisalService> logger)
{
    public const int MaxDetailsLength = 4_000;
    public const int MaxPhotoBytes = 8_000_000;

    public async Task<AppResult<IdentificationDto>> IdentifyAsync(IdentifyInput input, CancellationToken cancellationToken)
    {
        string? details = string.IsNullOrWhiteSpace(input.Details) ? null : input.Details.Trim();
        string? givenBarcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode;
        string? barcode = NormalizeBarcode(givenBarcode);

        if (input.Photo is null && givenBarcode is null && details is null)
        {
            return AppResult<IdentificationDto>.Validation("Send a photo, a barcode, or what you know about the item.", "input_required");
        }

        if (givenBarcode is not null && barcode is null)
        {
            return AppResult<IdentificationDto>.Validation("A barcode is 8 to 14 digits.", "barcode_invalid");
        }

        if (details?.Length > MaxDetailsLength)
        {
            return AppResult<IdentificationDto>.Validation($"Keep item details under {MaxDetailsLength:N0} characters.", "details_too_long");
        }

        if (input.Photo is { } photo && (photo.Bytes.Length == 0 || photo.Bytes.Length > MaxPhotoBytes))
        {
            return AppResult<IdentificationDto>.Validation($"Photos must be under {MaxPhotoBytes / 1_000_000} MB.", "photo_too_large");
        }

        if (barcode is not null && input.Photo is null && details is null)
        {
            return await IdentifyBarcodeAsync(barcode, cancellationToken);
        }

        if (!vision.IsConfigured)
        {
            throw new VisionUnavailableException("vision_not_configured", "Photo and text identification is not set up on the server.");
        }

        string? context = barcode is null ? details : string.Join('\n', $"Barcode: {barcode}", details).Trim();
        VisionIdentifyResult result = await vision.IdentifyAsync(
            input.Photo is null ? null : Convert.ToBase64String(input.Photo.Bytes),
            input.Photo?.MediaType,
            context,
            cancellationToken);

        IdentificationDto? identified = await FromVisionAsync(result.Product, barcode, input.Photo is null, cancellationToken);
        return identified is null
            ? AppResult<IdentificationDto>.NotFound(
                input.Photo is null
                    ? "Couldn't tell what this is from the details. Add the brand, model or what's printed on it."
                    : "Couldn't tell what this is. Retake the photo closer and in good light, or type what you know.",
                "not_identified")
            : AppResult<IdentificationDto>.Ok(identified);
    }

    public async Task<IReadOnlyList<AppraisalSnapshotDto>> HistoryAsync(Guid inventoryItemId, CancellationToken cancellationToken) =>
        await history.ListForItemAsync(currentUser.OrganizationId, inventoryItemId, 20, cancellationToken);

    public async Task<AppResult<AppraisalDto>> AppraiseAsync(AppraiseRequest request, CancellationToken cancellationToken)
    {
        string? invalid = Validate(request);
        if (invalid is not null)
        {
            return AppResult<AppraisalDto>.Validation(invalid);
        }

        if (request.InventoryItemId is { } itemId
            && !await history.ItemExistsAsync(currentUser.OrganizationId, itemId, cancellationToken))
        {
            return AppResult<AppraisalDto>.NotFound("That item is not in this workspace.");
        }

        ItemFacts item = new(
            request.Title.Trim(),
            Clean(request.Brand),
            Clean(request.Model),
            Clean(request.Category),
            Clean(request.Color),
            Clean(request.Condition),
            (request.Identifiers ?? []).Select(identifier => identifier.Trim()).Where(identifier => identifier.Length > 0).ToArray(),
            request.Certainty ?? IdentificationCertainty.Medium);
        string searchText = SearchTextFor(item);
        CompQuery query = new(currentUser.OrganizationId, item, searchText, NormalizeBarcode(request.Barcode), request.ProductIdentityId);

        SourceRun[] runs = await Task.WhenAll(sources.Select(source => RunAsync(source, query, cancellationToken)));

        List<Comp> comps = Deduplicate(runs.SelectMany(run => run.Result?.Comps ?? []));
        ModelEstimate? estimate = runs.Select(run => run.Result?.Estimate).FirstOrDefault(found => found is not null)
            ?? ToModelEstimate(request.Estimate);
        DateTimeOffset now = clock.GetUtcNow();
        PriceResult price = CompPricing.Price(item, comps, estimate, now);
        if (request.InventoryItemId is { } appraisedItem)
        {
            await history.RecordAsync(currentUser.OrganizationId, appraisedItem, request.ProductIdentityId, price, cancellationToken);
        }

        return AppResult<AppraisalDto>.Ok(new AppraisalDto(
            searchText,
            new PriceDto(price.Basis, price.QuickSale, price.List, price.High, price.Confidence, price.Level, price.Reasons),
            price.Comps.Select(ToDto).ToArray(),
            estimate is null ? null : new EstimateDto(estimate.Low, estimate.High, estimate.Confidence, estimate.Rationale, estimate.IsWide),
            runs.Select(run => run.Report).ToArray(),
            CompSearchLinks.For(searchText),
            runs.SelectMany(run => run.Result?.WebSources ?? []).DistinctBy(source => source.Url).Take(8).ToArray(),
            now));
    }

    private sealed record SourceRun(CompSourceReportDto Report, CompSourceResult? Result);

    private async Task<SourceRun> RunAsync(ICompSource source, CompQuery query, CancellationToken cancellationToken)
    {
        if (!source.IsConfigured)
        {
            return new SourceRun(new CompSourceReportDto(source.Name, CompSourceStatus.Off, 0, "Not set up."), null);
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(source.Deadline);
        try
        {
            CompSourceResult result = await source.FindAsync(query, deadline.Token);
            CompSourceStatus status = result.Comps.Count > 0 || result.Estimate is not null ? CompSourceStatus.Found : CompSourceStatus.Empty;
            return new SourceRun(new CompSourceReportDto(source.Name, status, result.Comps.Count, null), result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("{Source} took longer than {Deadline} for {Query}", source.Name, source.Deadline, query.SearchText);
            return new SourceRun(new CompSourceReportDto(source.Name, CompSourceStatus.Failed, 0, "Took too long to answer."), null);
        }
        catch (CompSourceUnavailableException exception)
        {
            return new SourceRun(new CompSourceReportDto(source.Name, CompSourceStatus.Failed, 0, exception.Message), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "{Source} failed for {Query}", source.Name, query.SearchText);
            return new SourceRun(new CompSourceReportDto(source.Name, CompSourceStatus.Failed, 0, "Could not be reached."), null);
        }
    }

    private async Task<AppResult<IdentificationDto>> IdentifyBarcodeAsync(string barcode, CancellationToken cancellationToken)
    {
        CatalogProduct? known = await catalog.FindByBarcodeAsync(currentUser.OrganizationId, barcode, cancellationToken);
        if (known is not null)
        {
            return AppResult<IdentificationDto>.Ok(new IdentificationDto(
                known.Title, known.Brand, known.Model, known.Category, Color: null, Condition: null, ConditionGrade: null,
                barcode, [barcode], IdentificationCertainty.High, IdentificationSource.Catalog, known.ProductIdentityId,
                ImageUrl: null, MissingInformation: [], Estimate: null));
        }

        BarcodeLookupResult? registered = await barcodes.LookupAsync(barcode, cancellationToken);
        if (string.IsNullOrWhiteSpace(registered?.Title))
        {
            return AppResult<IdentificationDto>.NotFound(
                "This barcode isn't in any registry we can reach. Take a photo or type what it is.",
                "barcode_unknown");
        }

        return AppResult<IdentificationDto>.Ok(new IdentificationDto(
            registered.Title.Trim(), Clean(registered.Brand), Model: null, Clean(registered.Category), Color: null,
            Condition: null, ConditionGrade: null, barcode, [barcode], IdentificationCertainty.High,
            IdentificationSource.Barcode, ProductIdentityId: null, Clean(registered.ImageUrl), MissingInformation: [],
            Estimate: null));
    }

    private async Task<IdentificationDto?> FromVisionAsync(
        ProductIdentification? product,
        string? givenBarcode,
        bool fromDetailsOnly,
        CancellationToken cancellationToken)
    {
        if (product is null)
        {
            return null;
        }

        string? title = Clean(product.Name)
            ?? Clean(string.Join(' ', new[] { product.Brand, product.Model }.Where(part => !string.IsNullOrWhiteSpace(part))))
            ?? Clean(product.Category);
        if (title is null)
        {
            return null;
        }

        string printed = product.IdentifyingText ?? string.Empty;
        string? barcode = givenBarcode;
        if (barcode is null && BarcodeInText().Match(printed) is { Success: true } printedBarcode)
        {
            barcode = printedBarcode.Value;
        }

        List<string> identifiers = [.. CardNumberInText().Matches(printed).Select(found => found.Value)];
        if (Clean(product.Model) is { } model)
        {
            identifiers.Add(model);
        }

        if (barcode is not null)
        {
            identifiers.Add(barcode);
        }

        CatalogProduct? known = barcode is null ? null : await catalog.FindByBarcodeAsync(currentUser.OrganizationId, barcode, cancellationToken);
        EstimateDto? estimate = product.EstimatedResaleHigh is > 0
            ? new EstimateDto(
                product.EstimatedResaleLow ?? product.EstimatedResaleHigh.Value,
                product.EstimatedResaleHigh.Value,
                product.ValuationConfidence ?? "low",
                Clean(product.Rationale),
                string.Equals(product.PriceRangeType, "wide", StringComparison.OrdinalIgnoreCase))
            : null;

        return new IdentificationDto(
            title,
            Clean(product.Brand),
            Clean(product.Model),
            Clean(product.Category),
            Clean(product.Color),
            Clean(product.Condition),
            Clean(product.ConditionQuality),
            barcode,
            identifiers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            product.Confidence switch
            {
                "high" => IdentificationCertainty.High,
                "medium" => IdentificationCertainty.Medium,
                _ => IdentificationCertainty.Low,
            },
            fromDetailsOnly ? IdentificationSource.Details : IdentificationSource.Photo,
            known?.ProductIdentityId,
            ImageUrl: null,
            product.MissingInformation ?? [],
            estimate);
    }

    private static string SearchTextFor(ItemFacts item)
    {
        string text = item.Brand is not null && !item.Title.Contains(item.Brand, StringComparison.OrdinalIgnoreCase)
            ? $"{item.Brand} {item.Title}"
            : item.Title;
        if (item.Model is not null && !text.Contains(item.Model, StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} {item.Model}";
        }

        // Set, edition, language and card number sharpen a search; a barcode's digits only scatter it.
        foreach (string identifier in item.Identifiers)
        {
            bool barcodeDigits = identifier.Length >= 8 && identifier.All(char.IsDigit);
            if (!barcodeDigits && !text.Contains(identifier, StringComparison.OrdinalIgnoreCase)
                && text.Length + identifier.Length + 1 <= 120)
            {
                text = $"{text} {identifier}";
            }
        }

        return text.Length <= 120 ? text : text[..120].TrimEnd();
    }

    private static string? Validate(AppraiseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "Say what the item is before pricing it.";
        }

        if (request.Title.Length > 300)
        {
            return "Keep the title under 300 characters.";
        }

        string?[] shortFields = [request.Brand, request.Model, request.Category, request.Color, request.Condition];
        if (shortFields.Any(field => field?.Length > 160))
        {
            return "Brand, model, category, color and condition must each be under 160 characters.";
        }

        if (request.Identifiers is { } identifiers && (identifiers.Count > 10 || identifiers.Any(identifier => identifier.Length > 80)))
        {
            return "Send at most 10 identifiers of up to 80 characters each.";
        }

        if (request.Estimate is { } estimate && (estimate.Low < 0 || estimate.High <= 0 || estimate.Low > estimate.High))
        {
            return "An estimate needs a low no greater than its high.";
        }

        return null;
    }

    private static List<Comp> Deduplicate(IEnumerable<Comp> comps) => comps
        .DistinctBy(comp => comp.Url is { Length: > 0 } url
            ? url.ToLowerInvariant()
            : $"{comp.Source}|{comp.Title}|{comp.Price}|{comp.Date:yyyy-MM-dd}".ToLowerInvariant())
        .ToList();

    private static ModelEstimate? ToModelEstimate(EstimateDto? estimate) =>
        estimate is null ? null : new ModelEstimate(estimate.Low, estimate.High, estimate.Rationale, estimate.Confidence, estimate.IsWide);

    private static CompDto ToDto(AssessedComp assessed) => new(
        assessed.Comp.Kind,
        assessed.Comp.Source,
        assessed.Comp.Title,
        assessed.Comp.Price,
        assessed.Comp.Shipping,
        assessed.Comp.Condition,
        assessed.Comp.Url,
        assessed.Comp.Date,
        assessed.Comp.Verified,
        assessed.Included,
        assessed.ExcludedBecause);

    private static string? NormalizeBarcode(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string digits = new(value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 8 and <= 14 ? digits : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"(?<!\d)\d{12,13}(?!\d)")]
    private static partial Regex BarcodeInText();

    [GeneratedRegex(@"\b\d{1,4}/\d{1,4}\b")]
    private static partial Regex CardNumberInText();
}
