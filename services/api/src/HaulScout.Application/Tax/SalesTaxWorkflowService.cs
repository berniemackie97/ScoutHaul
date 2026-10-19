using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Tax;

namespace HaulScout.Application.Tax;

/// <summary>
/// Florida sales and use tax: the registration, every period owed, the DR-15 worksheet for each, and the record of
/// what was filed. Periods are computed oldest first so carried-forward deductions and credits flow into the next.
/// </summary>
public sealed class SalesTaxWorkflowService(
    ISalesTaxStore store,
    ICurrentUserContext currentUser,
    IAuditTrail audit,
    TimeProvider clock)
{
    private const string Jurisdiction = SalesTaxRegistration.Florida;
    private const string FormVersion = "DR-15 R. 01/25";
    private const int DueSoonDays = 7;

    public static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly IReadOnlyList<string> Sources =
    [
        "Form DR-15 R. 01/25 and instructions DR-15N R. 10/25 (Rule 12A-1.097, F.A.C.)",
        $"Discretionary sales surtax: {FloridaSurtax.TableSource}",
        $"Interest: {FloridaInterest.TableSource}; deadlines: DR-659 (2026)",
        "Marketplace sales excluded: s. 212.05965(4)(a), F.S.; tax collected held in trust: s. 213.756(1), F.S.",
        "Separately stated optional delivery charges: Rule 12A-1.045, F.A.C.",
    ];

    // ── Profile ────────────────────────────────────────────────────────────

    public async Task<SalesTaxProfileDto> GetProfileAsync(CancellationToken cancellationToken)
    {
        SalesTaxRegistration? registration = await store.FindRegistrationAsync(currentUser.OrganizationId, Jurisdiction, cancellationToken);
        string zone = await TimeZoneIdAsync(cancellationToken);
        return ToProfile(registration, zone);
    }

    public async Task<AppResult<SalesTaxProfileDto>> SaveProfileAsync(SaveSalesTaxProfileRequest request, CancellationToken cancellationToken)
    {
        string? legalName = Trim(request.LegalName);
        if (legalName is null) return AppResult<SalesTaxProfileDto>.Validation("Enter the legal business name exactly as it appears on the certificate.");

        string? county = FloridaSurtax.Canonical(request.County);
        if (county is null) return AppResult<SalesTaxProfileDto>.Validation("Choose the Florida county the business is located in.");

        string? certificate = Trim(request.CertificateNumber);
        if (certificate is not null && certificate.Count(char.IsDigit) != 13)
        {
            return AppResult<SalesTaxProfileDto>.Validation("A Florida sales tax certificate number has 13 digits (for example 78-8012345678-9).");
        }

        if (certificate is not null && certificate.Any(character => !char.IsDigit(character) && character != '-' && character != ' '))
        {
            return AppResult<SalesTaxProfileDto>.Validation("The certificate number can contain only digits and dashes.");
        }

        if (Trim(request.PostalCode) is string zip && !IsZip(zip))
        {
            return AppResult<SalesTaxProfileDto>.Validation("Enter a 5-digit ZIP code or ZIP+4.");
        }

        DateOnly today = await TodayAsync(cancellationToken);
        if (request.RegisteredOn > today) return AppResult<SalesTaxProfileDto>.Validation("The registration date cannot be in the future.");
        if (request.ClosedOn is DateOnly closed && (closed < request.RegisteredOn || closed > today))
        {
            return AppResult<SalesTaxProfileDto>.Validation("The closing date must be between the registration date and today.");
        }

        if (ValidateSchedule(request.Frequencies, request.RegisteredOn) is string scheduleError)
        {
            return AppResult<SalesTaxProfileDto>.Validation(scheduleError);
        }

        Guid organizationId = currentUser.OrganizationId;
        SalesTaxRegistration? registration = await store.FindRegistrationAsync(organizationId, Jurisdiction, cancellationToken);
        bool created = registration is null;
        if (registration is null)
        {
            registration = new SalesTaxRegistration { OrganizationId = organizationId, LegalName = legalName, County = county };
            await store.AddRegistrationAsync(registration, cancellationToken);
        }

        registration.CertificateNumber = certificate;
        registration.BusinessPartnerNumber = Trim(request.BusinessPartnerNumber);
        registration.LegalName = legalName;
        registration.DoingBusinessAs = Trim(request.DoingBusinessAs);
        registration.AddressLine1 = Trim(request.AddressLine1);
        registration.AddressLine2 = Trim(request.AddressLine2);
        registration.City = Trim(request.City);
        registration.PostalCode = Trim(request.PostalCode);
        registration.County = county;
        registration.RegisteredOn = request.RegisteredOn;
        registration.ClosedOn = request.ClosedOn;
        registration.FilesElectronically = request.FilesElectronically;
        registration.DonateCollectionAllowance = request.DonateCollectionAllowance;
        registration.DeliveryChargesOptional = request.DeliveryChargesOptional;
        registration.Notes = Trim(request.Notes);
        // Updated in place by start date: deleting and re-adding a row with the same start would trip the unique index.
        List<SalesTaxFrequencyChange> dropped = registration.Frequencies
            .Where(existing => request.Frequencies.All(item => item.EffectiveFrom != existing.EffectiveFrom))
            .ToList();
        store.RemoveFrequencies(dropped);
        foreach (SalesTaxFrequencyChange change in dropped) registration.Frequencies.Remove(change);

        foreach (SalesTaxFrequencyDto frequency in request.Frequencies)
        {
            SalesTaxFrequencyChange? change = registration.Frequencies.FirstOrDefault(existing => existing.EffectiveFrom == frequency.EffectiveFrom);
            if (change is null)
            {
                change = new SalesTaxFrequencyChange { OrganizationId = organizationId, RegistrationId = registration.Id, EffectiveFrom = frequency.EffectiveFrom };
                store.AddFrequency(change);
                if (!registration.Frequencies.Contains(change)) registration.Frequencies.Add(change);
            }

            change.Frequency = frequency.Frequency;
            change.Alignment = frequency.Alignment;
        }

        await audit.RecordAsync(nameof(SalesTaxRegistration), registration.Id, created ? "SalesTaxRegistrationCreated" : "SalesTaxRegistrationUpdated", new
        {
            registration.CertificateNumber,
            registration.LegalName,
            registration.County,
            registration.RegisteredOn,
            registration.ClosedOn,
            registration.FilesElectronically,
            registration.DeliveryChargesOptional,
            Frequencies = request.Frequencies,
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SalesTaxProfileDto>.Ok(ToProfile(registration, await TimeZoneIdAsync(cancellationToken)));
    }

    /// <summary>The periods a registration would generate and how each stands today, from unsaved form values.</summary>
    public async Task<SalesTaxSchedulePreviewDto> PreviewAsync(SalesTaxSchedulePreviewRequest request, CancellationToken cancellationToken)
    {
        DateOnly today = await TodayAsync(cancellationToken);
        string? county = FloridaSurtax.Canonical(request.County);
        decimal? surtax = county is null ? null : FloridaSurtax.RateOn(county, today);
        SalesTaxSchedulePreviewDto Problem(string message) =>
            new(message, county, surtax, surtax + FloridaSurtax.StateRate, today, []);

        if (request.RegisteredOn is not DateOnly registeredOn) return Problem("Enter the registration date to see the returns it requires.");
        if (registeredOn > today) return Problem("The registration date cannot be in the future.");
        if (request.ClosedOn is DateOnly closed && (closed < registeredOn || closed > today))
        {
            return Problem("The closing date must be between the registration date and today.");
        }

        IReadOnlyList<SalesTaxFrequencyDto> frequencies = request.Frequencies ?? [];
        if (ValidateSchedule(frequencies, registeredOn) is string scheduleError) return Problem(scheduleError);

        List<SalesTaxFrequencySpan> schedule = frequencies
            .Select(item => new SalesTaxFrequencySpan(item.EffectiveFrom, item.Frequency, item.Alignment))
            .ToList();
        IReadOnlyList<SalesTaxPeriod> periods = FloridaFilingCalendar.Periods(registeredOn, schedule, today, request.ClosedOn);
        IReadOnlyList<SalesTaxReturn> returns = await store.ListReturnsAsync(currentUser.OrganizationId, Jurisdiction, cancellationToken);

        List<SalesTaxPreviewPeriodDto> preview = periods
            .Select(period =>
            {
                SalesTaxReturn? effective = returns
                    .Where(filed => !filed.IsVoided && filed.PeriodStart == period.Start && filed.PeriodEnd == period.End)
                    .MaxBy(filed => filed.CreatedAt);
                return new SalesTaxPreviewPeriodDto(
                    period.Key,
                    Label(period),
                    period.Start,
                    period.End,
                    period.LateAfter,
                    period.ElectronicPaymentDeadline,
                    StatusOf(period, effective, changedSinceFiled: false, today),
                    period.LateAfter.DayNumber - today.DayNumber);
            })
            .Reverse()
            .ToList();

        return new SalesTaxSchedulePreviewDto(null, county, surtax, surtax + FloridaSurtax.StateRate, today, preview);
    }

    // ── Overview and worksheet ─────────────────────────────────────────────

    public async Task<SalesTaxOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        Ledger? ledger = await LoadLedgerAsync(null, cancellationToken);
        string zone = await TimeZoneIdAsync(cancellationToken);
        DateOnly today = await TodayAsync(cancellationToken);
        if (ledger is null)
        {
            return new SalesTaxOverviewDto(ToProfile(null, zone), today, null, 0, null, 0m, [],
                [new SalesTaxNoticeDto(Dr15IssueSeverity.Blocking, "not_configured",
                    "Add the Florida registration (certificate, county, registration date and filing frequency) to see the returns owed.")]);
        }

        List<SalesTaxPeriodSummaryDto> summaries = ledger.Periods.Select(item => item.Summary).ToList();
        SalesTaxPeriodSummaryDto? nextDue =
            summaries.FirstOrDefault(summary => summary.Status is SalesTaxPeriodStatus.Overdue)
            ?? summaries.FirstOrDefault(summary => summary.Status is SalesTaxPeriodStatus.DueSoon or SalesTaxPeriodStatus.ReadyToFile)
            ?? summaries.FirstOrDefault(summary => summary.Status is SalesTaxPeriodStatus.Open);

        DateOnly yearAgo = today.AddYears(-1);
        decimal lastFourQuarters = ledger.Periods
            .Where(item => item.Period.End > yearAgo && item.Period.End < today)
            .Sum(item => item.Effective?.AmountDue ?? item.Computation.Result.Lines.Line10);

        var notices = new List<SalesTaxNoticeDto>();
        if (!IsFloridaZone(zone))
        {
            notices.Add(new(Dr15IssueSeverity.Warning, "time_zone",
                $"The business time zone is {zone}. Sales near midnight can land in the wrong Florida day; set it to America/New_York in Store settings."));
        }

        if (ledger.Registration.CertificateNumber is null)
        {
            notices.Add(new(Dr15IssueSeverity.Warning, "certificate_missing", "Add the 13-digit certificate number so it prints on the worksheet."));
        }

        foreach (SalesTaxPeriodSummaryDto overdue in summaries.Where(summary => summary.Status == SalesTaxPeriodStatus.Overdue))
        {
            notices.Add(new(Dr15IssueSeverity.Blocking, "overdue",
                $"{overdue.Label} was due {overdue.LateAfter:MMM d, yyyy} and has no filing recorded. Filing today: {overdue.AmountDueWithReturn:C} including the late penalty.",
                overdue.Key));
        }

        foreach (SalesTaxPeriodSummaryDto changed in summaries.Where(summary => summary.Status == SalesTaxPeriodStatus.ChangedSinceFiled))
        {
            notices.Add(new(Dr15IssueSeverity.Warning, "changed_since_filed",
                $"{changed.Label} no longer matches what was filed. Review the differences and file an amended return if they are real.", changed.Key));
        }

        foreach (SalesTaxReturn orphan in ledger.UnmatchedFilings)
        {
            notices.Add(new(Dr15IssueSeverity.Warning, "filing_without_period",
                $"A return filed {orphan.FiledOn:MMM d, yyyy} for {orphan.PeriodStart:MM/dd/yyyy}–{orphan.PeriodEnd:MM/dd/yyyy} no longer matches any period. Check the registration date and filing frequency."));
        }

        if (!FloridaSurtax.Covers(today))
        {
            notices.Add(new(Dr15IssueSeverity.Warning, "surtax_table_stale",
                $"Surtax rates are from {FloridaSurtax.TableSource}. Load the {today.Year} DR-15DSS before filing {today.Year} periods."));
        }

        SalesTaxFrequency? eligible = null;
        SalesTaxFrequency current = ledger.Registration.Schedule().Last().Frequency;
        if (ledger.Periods.Count(item => item.Period.End < today) >= 4)
        {
            eligible = FloridaFilingCalendar.EligibleFrequency(lastFourQuarters);
            if ((int)eligible > (int)current)
            {
                notices.Add(new(Dr15IssueSeverity.Info, "frequency_eligible",
                    $"With {lastFourQuarters:C} remitted over the last year, s. 212.11(1)(c) allows {eligible.Value.ToString().ToLowerInvariant()} filing. Only DOR changes your frequency."));
            }
        }

        return new SalesTaxOverviewDto(
            ToProfile(ledger.Registration, zone),
            today,
            nextDue,
            summaries.Count(summary => summary.Status == SalesTaxPeriodStatus.Overdue),
            eligible,
            lastFourQuarters,
            summaries.AsEnumerable().Reverse().ToList(),
            notices);
    }

    /// <param name="filedOn">Projects the return as if filed (and paid) on this date. Defaults to the earliest on-time date.</param>
    public async Task<AppResult<SalesTaxWorksheetDto>> GetWorksheetAsync(string periodKey, DateOnly? filedOn, CancellationToken cancellationToken)
    {
        Ledger? ledger = await LoadLedgerAsync(new Projection(periodKey, filedOn, null, null, null), cancellationToken);
        if (ledger is null) return AppResult<SalesTaxWorksheetDto>.NotFound("Set up the Florida registration first.");

        LedgerPeriod? item = ledger.Periods.FirstOrDefault(period => period.Period.Key == periodKey);
        return item is null
            ? AppResult<SalesTaxWorksheetDto>.NotFound("No such reporting period.")
            : AppResult<SalesTaxWorksheetDto>.Ok(Worksheet(ledger, item));
    }

    // ── Filing ─────────────────────────────────────────────────────────────

    public async Task<AppResult<SalesTaxWorksheetDto>> FileAsync(string periodKey, FileSalesTaxReturnRequest request, CancellationToken cancellationToken)
    {
        DateOnly today = await TodayAsync(cancellationToken);
        if (request.FiledOn > today) return AppResult<SalesTaxWorksheetDto>.Validation("The filing date cannot be in the future.");
        if (request.PaidOn is DateOnly paid && paid < request.FiledOn.AddDays(-31))
        {
            return AppResult<SalesTaxWorksheetDto>.Validation("The payment date looks wrong: it is more than a month before the filing date.");
        }

        if (request.AmountPaid < 0) return AppResult<SalesTaxWorksheetDto>.Validation("The amount paid cannot be negative.");
        if (!Enum.IsDefined(request.Method)) return AppResult<SalesTaxWorksheetDto>.Validation("Choose how the return was filed.");
        string? confirmation = Trim(request.ConfirmationNumber);
        if (request.Method != SalesTaxFilingMethod.Paper && confirmation is null)
        {
            return AppResult<SalesTaxWorksheetDto>.Validation("Enter the confirmation number eServices or telefile gave you; DOR asks you to keep it.");
        }

        Ledger? ledger = await LoadLedgerAsync(
            new Projection(periodKey, request.FiledOn, request.PaidOn, request.Method == SalesTaxFilingMethod.Electronic, request.DonateCollectionAllowance),
            cancellationToken);
        if (ledger is null) return AppResult<SalesTaxWorksheetDto>.NotFound("Set up the Florida registration first.");

        LedgerPeriod? item = ledger.Periods.FirstOrDefault(period => period.Period.Key == periodKey);
        if (item is null) return AppResult<SalesTaxWorksheetDto>.NotFound("No such reporting period.");
        if (request.FiledOn <= item.Period.End)
        {
            return AppResult<SalesTaxWorksheetDto>.Validation($"A return for this period can be filed from {item.Period.End.AddDays(1):MMM d, yyyy}.");
        }

        if (!string.Equals(request.ExpectedFingerprint, item.Computation.Fingerprint, StringComparison.Ordinal))
        {
            return AppResult<SalesTaxWorksheetDto>.Conflict("The numbers changed since you reviewed them. Reload the worksheet and check it again before recording the filing.");
        }

        Dr15Result result = item.Computation.Result;
        if (item.Computation.Issues.Any(issue => issue.Severity == Dr15IssueSeverity.Blocking))
        {
            return AppResult<SalesTaxWorksheetDto>.Validation("Resolve the blocking items on the worksheet before recording the filing.");
        }

        int warnings = item.Computation.Issues.Count(issue => issue.Severity == Dr15IssueSeverity.Warning);
        if (request.AcknowledgedWarnings < warnings)
        {
            return AppResult<SalesTaxWorksheetDto>.Validation($"Review and acknowledge the {warnings} warning(s) on the worksheet first.");
        }

        if (request.Method == SalesTaxFilingMethod.Telefile && (result.Lines.Line5 != 0 || result.Lines.Line6 != 0 || result.Lines.Line8 != 0))
        {
            return AppResult<SalesTaxWorksheetDto>.Validation("Telefile is only for returns with no tax due and no deductions or credits (DR-15N).");
        }

        SalesTaxReturn? effective = item.Effective;
        if (effective is not null && !request.Amended)
        {
            return AppResult<SalesTaxWorksheetDto>.Conflict("A return is already recorded for this period. File an amended replacement instead.");
        }

        if (effective is null && request.Amended)
        {
            return AppResult<SalesTaxWorksheetDto>.Validation("There is no recorded return to amend for this period.");
        }

        SalesTaxWorksheetDto snapshot = Worksheet(ledger, item);
        var filed = new SalesTaxReturn
        {
            OrganizationId = currentUser.OrganizationId,
            RegistrationId = ledger.Registration.Id,
            PeriodStart = item.Period.Start,
            PeriodEnd = item.Period.End,
            Frequency = item.Period.Frequency,
            IsFinal = item.Period.IsFinal,
            Kind = request.Amended ? SalesTaxReturnKind.AmendedReplacement : SalesTaxReturnKind.Original,
            SupersedesReturnId = effective?.Id,
            FiledOn = request.FiledOn,
            PaidOn = request.PaidOn ?? (result.Lines.Line14 > 0 ? request.FiledOn : null),
            Method = request.Method,
            ConfirmationNumber = confirmation,
            AmountPaid = request.AmountPaid,
            DonatedCollectionAllowance = request.DonateCollectionAllowance,
            GrossSales = result.Lines.LineA.Gross ?? 0m,
            TaxableAmount = result.Lines.LineA.Taxable + result.Lines.LineB.Taxable,
            TotalTaxDue = result.Lines.Line5,
            AmountDue = result.Lines.Line10,
            AmountDueWithReturn = result.Lines.Line14,
            DeductionsCarriedOut = result.DeductionsCarriedOut,
            CreditsCarriedOut = result.CreditsCarriedOut,
            WarningsAcknowledged = warnings,
            SourceFingerprint = item.Computation.Fingerprint,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJson),
            Notes = Trim(request.Notes),
            FiledByUserId = currentUser.UserId,
        };

        await store.AddReturnAsync(filed, cancellationToken);
        await audit.RecordAsync(nameof(SalesTaxReturn), filed.Id, request.Amended ? "SalesTaxReturnAmended" : "SalesTaxReturnFiled", new
        {
            Period = item.Period.Key,
            filed.FiledOn,
            filed.PaidOn,
            filed.Method,
            filed.ConfirmationNumber,
            filed.AmountPaid,
            filed.TotalTaxDue,
            filed.AmountDueWithReturn,
            filed.SupersedesReturnId,
            filed.SourceFingerprint,
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return await GetWorksheetAsync(periodKey, null, cancellationToken);
    }

    public async Task<AppResult> VoidAsync(Guid returnId, VoidSalesTaxReturnRequest request, CancellationToken cancellationToken)
    {
        string? reason = Trim(request.Reason);
        if (reason is null) return AppResult.Validation("Say why this filing record is being voided.");

        SalesTaxReturn? filed = await store.FindReturnAsync(currentUser.OrganizationId, returnId, cancellationToken);
        if (filed is null) return AppResult.NotFound();
        if (filed.IsVoided) return AppResult.Conflict("This filing record is already voided.");

        filed.VoidedAt = clock.GetUtcNow();
        filed.VoidReason = reason;
        filed.VoidedByUserId = currentUser.UserId;
        await audit.RecordAsync(nameof(SalesTaxReturn), filed.Id, "SalesTaxReturnVoided", new { reason, filed.PeriodStart, filed.PeriodEnd }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    // ── Use tax, adjustments, sale decisions ───────────────────────────────

    public async Task<AppResult<SalesTaxUseEntryDto>> SaveUseEntryAsync(Guid? id, SaveSalesTaxUseEntryRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        string? description = Trim(request.Description);
        if (description is null) return AppResult<SalesTaxUseEntryDto>.Validation("Describe what was bought and used.");
        if (request.Amount <= 0) return AppResult<SalesTaxUseEntryDto>.Validation("The purchase amount must be more than zero.");
        if (request.OtherStateTaxPaid < 0) return AppResult<SalesTaxUseEntryDto>.Validation("Tax paid to another state cannot be negative.");
        if (request.OccurredOn > await TodayAsync(cancellationToken)) return AppResult<SalesTaxUseEntryDto>.Validation("The date cannot be in the future.");

        string? county = null;
        if (Trim(request.County) is string rawCounty)
        {
            county = FloridaSurtax.Canonical(rawCounty);
            if (county is null) return AppResult<SalesTaxUseEntryDto>.Validation("Choose a Florida county, or leave it blank for the business county.");
        }

        if (request.ExpenseId is Guid expenseId && !await store.ExpenseExistsAsync(organizationId, expenseId, cancellationToken))
        {
            return AppResult<SalesTaxUseEntryDto>.Validation("That expense does not exist.");
        }

        if (request.InventoryItemId is Guid itemId && !await store.InventoryItemExistsAsync(organizationId, itemId, cancellationToken))
        {
            return AppResult<SalesTaxUseEntryDto>.Validation("That inventory item does not exist.");
        }

        SalesTaxUseEntry? entry;
        if (id is Guid existing)
        {
            entry = await store.FindUseEntryAsync(organizationId, existing, cancellationToken);
            if (entry is null) return AppResult<SalesTaxUseEntryDto>.NotFound();
        }
        else
        {
            entry = new SalesTaxUseEntry { OrganizationId = organizationId, Description = description, RecordedByUserId = currentUser.UserId };
            await store.AddUseEntryAsync(entry, cancellationToken);
        }

        entry.OccurredOn = request.OccurredOn;
        entry.Description = description;
        entry.Amount = request.Amount;
        entry.County = county;
        entry.OtherStateTaxPaid = request.OtherStateTaxPaid;
        entry.ExpenseId = request.ExpenseId;
        entry.InventoryItemId = request.InventoryItemId;
        entry.Notes = Trim(request.Notes);
        await audit.RecordAsync(nameof(SalesTaxUseEntry), entry.Id, id is null ? "UseTaxRecorded" : "UseTaxUpdated", request, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SalesTaxUseEntryDto>.Ok(SalesTaxPeriodBuilder.ToDto(entry));
    }

    public async Task<AppResult> DeleteUseEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        SalesTaxUseEntry? entry = await store.FindUseEntryAsync(currentUser.OrganizationId, id, cancellationToken);
        if (entry is null) return AppResult.NotFound();

        store.RemoveUseEntry(entry);
        await audit.RecordAsync(nameof(SalesTaxUseEntry), entry.Id, "UseTaxDeleted", SalesTaxPeriodBuilder.ToDto(entry), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<SalesTaxAdjustmentDto>> AddAdjustmentAsync(SaveSalesTaxAdjustmentRequest request, CancellationToken cancellationToken)
    {
        string? description = Trim(request.Description);
        if (description is null) return AppResult<SalesTaxAdjustmentDto>.Validation("Describe the adjustment and where it comes from (credit memo number, statute).");
        if (request.Amount <= 0) return AppResult<SalesTaxAdjustmentDto>.Validation("The amount must be more than zero.");
        if (!Enum.IsDefined(request.Kind)) return AppResult<SalesTaxAdjustmentDto>.Validation("Choose the line the adjustment belongs on.");

        Ledger? ledger = await LoadLedgerAsync(null, cancellationToken);
        SalesTaxPeriod? period = ledger?.Periods.Select(item => item.Period).FirstOrDefault(period => period.Key == request.PeriodKey);
        if (period is null) return AppResult<SalesTaxAdjustmentDto>.NotFound("No such reporting period.");

        var adjustment = new SalesTaxAdjustment
        {
            OrganizationId = currentUser.OrganizationId,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            Kind = request.Kind,
            Amount = request.Amount,
            Description = description,
            RecordedByUserId = currentUser.UserId,
        };
        await store.AddAdjustmentAsync(adjustment, cancellationToken);
        await audit.RecordAsync(nameof(SalesTaxAdjustment), adjustment.Id, "SalesTaxAdjustmentRecorded", request, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SalesTaxAdjustmentDto>.Ok(new SalesTaxAdjustmentDto(adjustment.Id, adjustment.Kind, adjustment.Amount, adjustment.Description));
    }

    public async Task<AppResult> DeleteAdjustmentAsync(Guid id, CancellationToken cancellationToken)
    {
        SalesTaxAdjustment? adjustment = await store.FindAdjustmentAsync(currentUser.OrganizationId, id, cancellationToken);
        if (adjustment is null) return AppResult.NotFound();

        store.RemoveAdjustment(adjustment);
        await audit.RecordAsync(nameof(SalesTaxAdjustment), adjustment.Id, "SalesTaxAdjustmentDeleted",
            new { adjustment.Kind, adjustment.Amount, adjustment.Description, adjustment.PeriodStart, adjustment.PeriodEnd }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult> SaveSaleDecisionAsync(Guid saleId, SaveSalesTaxSaleDecisionRequest request, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        if (await store.FindSaleAsync(organizationId, saleId, cancellationToken) is null) return AppResult.NotFound();

        string? county = null;
        switch (request.Destination)
        {
            case Dr15Destination.FloridaCounty:
                county = FloridaSurtax.Canonical(request.County);
                if (county is null) return AppResult.Validation("Choose the Florida county the item was delivered to.");
                break;
            case Dr15Destination.OutsideFlorida:
                break;
            default:
                return AppResult.Validation("Say whether the item was delivered in Florida or outside it.");
        }

        if (!Enum.IsDefined(request.Exemption)) return AppResult.Validation("Choose a valid exemption.");

        string? certificate = Trim(request.ExemptionCertificate);
        if (request.Exemption is Dr15Exemption.Resale or Dr15Exemption.ExemptOrganization && certificate is null)
        {
            return AppResult.Validation("An exempt sale needs the buyer's resale certificate or DR-14 number on file.");
        }

        if (request.Exemption == Dr15Exemption.Other && Trim(request.Notes) is null)
        {
            return AppResult.Validation("Explain the exemption in the notes.");
        }

        SalesTaxSaleDecision? decision = await store.FindDecisionAsync(organizationId, saleId, cancellationToken);
        if (decision is null)
        {
            decision = new SalesTaxSaleDecision { OrganizationId = organizationId, SaleId = saleId };
            await store.AddDecisionAsync(decision, cancellationToken);
        }

        decision.Destination = request.Destination;
        decision.County = county;
        decision.Exemption = request.Exemption;
        decision.ExemptionCertificate = certificate;
        decision.Notes = Trim(request.Notes);
        decision.DecidedByUserId = currentUser.UserId;
        await audit.RecordAsync(nameof(SalesTaxSaleDecision), decision.Id, "SalesTaxSaleDecided", new { saleId, request.Destination, county, request.Exemption, certificate }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteSaleDecisionAsync(Guid saleId, CancellationToken cancellationToken)
    {
        SalesTaxSaleDecision? decision = await store.FindDecisionAsync(currentUser.OrganizationId, saleId, cancellationToken);
        if (decision is null) return AppResult.NotFound();

        store.RemoveDecision(decision);
        await audit.RecordAsync(nameof(SalesTaxSaleDecision), decision.Id, "SalesTaxSaleDecisionCleared", new { saleId }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult> SetSaleCollectorAsync(Guid saleId, SetSaleTaxCollectorRequest request, CancellationToken cancellationToken)
    {
        string? reason = Trim(request.Reason);
        if (reason is null) return AppResult.Validation("Say why: who actually collected the tax on this sale.");

        Sale? sale = await store.FindSaleAsync(currentUser.OrganizationId, saleId, cancellationToken);
        if (sale is null) return AppResult.NotFound();
        if (sale.TaxRemittedByPlatform == request.MarketplaceCollected) return AppResult.Ok();

        bool before = sale.TaxRemittedByPlatform;
        sale.TaxRemittedByPlatform = request.MarketplaceCollected;
        await audit.RecordAsync(nameof(Sale), sale.Id, "SaleTaxCollectorChanged", new { before, after = request.MarketplaceCollected, reason, sale.Platform }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    // ── Ledger ─────────────────────────────────────────────────────────────

    private sealed record Projection(string PeriodKey, DateOnly? FiledOn, DateOnly? PaidOn, bool? Electronic, bool? Donate);

    private sealed record LedgerPeriod(
        SalesTaxPeriod Period,
        SalesTaxPeriodComputation Computation,
        SalesTaxReturn? Effective,
        IReadOnlyList<SalesTaxReturn> Filings,
        Dr15Timing Timing,
        bool Projected,
        SalesTaxPeriodSummaryDto Summary);

    private sealed record Ledger(
        SalesTaxRegistration Registration,
        TimeZoneInfo Zone,
        DateOnly Today,
        IReadOnlyList<LedgerPeriod> Periods,
        IReadOnlyList<SalesTaxReturn> UnmatchedFilings);

    private async Task<Ledger?> LoadLedgerAsync(Projection? projection, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SalesTaxRegistration? registration = await store.FindRegistrationAsync(organizationId, Jurisdiction, cancellationToken);
        if (registration is null || registration.Frequencies.Count == 0) return null;

        TimeZoneInfo zone = Organization.ZoneOf(await store.FindTimeZoneIdAsync(organizationId, cancellationToken));
        DateOnly today = SalesTaxPeriodBuilder.LocalDate(clock.GetUtcNow(), zone);
        IReadOnlyList<SalesTaxPeriod> periods = FloridaFilingCalendar.Periods(registration.RegisteredOn, registration.Schedule(), today, registration.ClosedOn);
        if (periods.Count == 0) return new Ledger(registration, zone, today, [], []);

        DateOnly from = periods[0].Start;
        DateOnly to = periods[^1].End;
        DateTimeOffset fromUtc = SalesTaxPeriodBuilder.StartOfDayUtc(from, zone);
        DateTimeOffset toUtc = SalesTaxPeriodBuilder.StartOfDayUtc(to.AddDays(1), zone);

        IReadOnlyList<SalesTaxSaleRow> sales = await store.ListSalesAsync(organizationId, fromUtc, toUtc, cancellationToken);
        IReadOnlyList<SalesTaxRefundRow> refunds = await store.ListRefundsAsync(organizationId, fromUtc, toUtc, cancellationToken);
        IReadOnlyList<SalesTaxUseEntry> uses = await store.ListUseEntriesAsync(organizationId, from, to, cancellationToken);
        IReadOnlyList<SalesTaxAdjustment> adjustments = await store.ListAdjustmentsAsync(organizationId, from, to, cancellationToken);
        IReadOnlyList<SalesTaxSaleDecision> decisionRows = await store.ListDecisionsAsync(organizationId, sales.Select(sale => sale.SaleId).ToList(), cancellationToken);
        Dictionary<Guid, SalesTaxSaleDecision> decisions = decisionRows.ToDictionary(decision => decision.SaleId);
        IReadOnlyList<SalesTaxReturn> returns = await store.ListReturnsAsync(organizationId, Jurisdiction, cancellationToken);

        var ledger = new List<LedgerPeriod>();
        decimal deductionsCarry = 0m;
        decimal creditsCarry = 0m;
        foreach (SalesTaxPeriod period in periods)
        {
            List<SalesTaxReturn> filings = returns
                .Where(filed => filed.PeriodStart == period.Start && filed.PeriodEnd == period.End)
                .OrderBy(filed => filed.CreatedAt)
                .ToList();
            SalesTaxReturn? effective = filings.LastOrDefault(filed => !filed.IsVoided);

            bool projecting = projection is not null && projection.PeriodKey == period.Key;
            (Dr15Timing timing, bool projected) = projecting && projection!.FiledOn is DateOnly filedOn
                ? (new Dr15Timing(filedOn, projection.PaidOn, projection.Electronic ?? registration.FilesElectronically, projection.Donate ?? registration.DonateCollectionAllowance), true)
                : effective is not null
                    ? (new Dr15Timing(effective.FiledOn, effective.PaidOn, effective.Method == SalesTaxFilingMethod.Electronic, effective.DonatedCollectionAllowance), false)
                    : (DefaultTiming(period, today, registration), true);

            SalesTaxPeriodComputation computation = SalesTaxPeriodBuilder.Compute(new SalesTaxPeriodInput(
                registration, period, zone, sales, refunds, uses, adjustments, decisions, deductionsCarry, creditsCarry, timing));

            deductionsCarry = effective?.DeductionsCarriedOut ?? computation.Result.DeductionsCarriedOut;
            creditsCarry = effective?.CreditsCarriedOut ?? computation.Result.CreditsCarriedOut;

            ledger.Add(new LedgerPeriod(period, computation, effective, filings, timing, projected,
                Summary(period, computation, effective, today)));
        }

        List<SalesTaxReturn> unmatched = returns
            .Where(filed => !filed.IsVoided && periods.All(period => period.Start != filed.PeriodStart || period.End != filed.PeriodEnd))
            .ToList();
        return new Ledger(registration, zone, today, ledger, unmatched);
    }

    /// <summary>An unfiled period is shown as if filed on its earliest on-time day, or today once that has passed.</summary>
    private static Dr15Timing DefaultTiming(SalesTaxPeriod period, DateOnly today, SalesTaxRegistration registration)
    {
        DateOnly earliest = period.End.AddDays(1);
        DateOnly filedOn = today < earliest ? earliest : today;
        return new Dr15Timing(filedOn, null, registration.FilesElectronically, registration.DonateCollectionAllowance);
    }

    private static SalesTaxPeriodStatus StatusOf(SalesTaxPeriod period, SalesTaxReturn? effective, bool changedSinceFiled, DateOnly today) =>
        effective is not null
            ? changedSinceFiled ? SalesTaxPeriodStatus.ChangedSinceFiled
              : effective.FiledOn > period.LateAfter ? SalesTaxPeriodStatus.FiledLate
              : SalesTaxPeriodStatus.Filed
            : today <= period.End ? SalesTaxPeriodStatus.Open
            : today > period.LateAfter ? SalesTaxPeriodStatus.Overdue
            : period.LateAfter.DayNumber - today.DayNumber <= DueSoonDays ? SalesTaxPeriodStatus.DueSoon
            : SalesTaxPeriodStatus.ReadyToFile;

    private static SalesTaxPeriodSummaryDto Summary(SalesTaxPeriod period, SalesTaxPeriodComputation computation, SalesTaxReturn? effective, DateOnly today)
    {
        Dr15Result result = computation.Result;
        SalesTaxPeriodStatus status = StatusOf(period, effective, effective is not null && effective.SourceFingerprint != computation.Fingerprint, today);

        SalesTaxFiledSummaryDto? filed = effective is null
            ? null
            : new SalesTaxFiledSummaryDto(effective.Id, effective.Kind, effective.FiledOn, effective.PaidOn, effective.Method,
                effective.ConfirmationNumber, effective.AmountPaid, effective.AmountDueWithReturn, effective.FiledOn > period.LateAfter);

        return new SalesTaxPeriodSummaryDto(
            period.Key,
            Label(period),
            period.Start,
            period.End,
            period.Frequency,
            period.IsFinal,
            period.DueDate,
            period.LateAfter,
            period.ElectronicPaymentDeadline,
            status,
            period.LateAfter.DayNumber - today.DayNumber,
            computation.ReportedSales.Count,
            result.Lines.LineA.Gross ?? 0m,
            computation.ExcludedSales.Count,
            computation.ExcludedChannels.Sum(channel => channel.GrossSales),
            result.Lines.Line5,
            effective?.AmountDueWithReturn ?? result.Lines.Line14,
            computation.Issues.Count(issue => issue.Severity == Dr15IssueSeverity.Blocking),
            computation.Issues.Count(issue => issue.Severity == Dr15IssueSeverity.Warning),
            filed);
    }

    private static SalesTaxWorksheetDto Worksheet(Ledger ledger, LedgerPeriod item)
    {
        SalesTaxRegistration registration = ledger.Registration;
        Dr15Result result = item.Computation.Result;
        Dr15Timeliness timeliness = result.Timeliness;

        IReadOnlyList<SalesTaxLineChangeDto> changes = [];
        if (item.Effective is not null && item.Effective.SourceFingerprint != item.Computation.Fingerprint)
        {
            changes = Differences(item.Effective, result.Lines);
        }

        return new SalesTaxWorksheetDto(
            item.Summary,
            new SalesTaxReturnHeaderDto(
                registration.CertificateNumber,
                registration.LegalName,
                registration.DoingBusinessAs,
                registration.AddressLine1,
                registration.AddressLine2,
                registration.City,
                registration.PostalCode,
                registration.County,
                result.Lines.SurtaxRate,
                FormVersion),
            result.Lines,
            new SalesTaxTimelinessDto(
                item.Timing.FiledOn,
                timeliness.PaidOn,
                item.Timing.Electronic,
                item.Projected,
                timeliness.ReturnLate,
                timeliness.PaymentLate,
                timeliness.CollectionAllowanceEarned,
                timeliness.Interest.Days,
                timeliness.Interest.Segments),
            item.Computation.Invoices,
            item.Computation.ReportedSales,
            item.Computation.ExcludedChannels,
            item.Computation.ExcludedSales,
            item.Computation.UseTax,
            item.Computation.Refunds,
            item.Computation.Adjustments,
            item.Computation.Carry,
            item.Computation.Issues,
            item.Computation.Fingerprint,
            item.Filings.Select(filed => new SalesTaxFiledReturnDto(
                filed.Id,
                filed.Kind,
                filed.SupersedesReturnId,
                filed.FiledOn,
                filed.PaidOn,
                filed.Method,
                filed.ConfirmationNumber,
                filed.AmountPaid,
                filed.DonatedCollectionAllowance,
                filed.TotalTaxDue,
                filed.AmountDueWithReturn,
                filed.WarningsAcknowledged,
                filed.Notes,
                filed.CreatedAt,
                filed.IsVoided,
                filed.VoidReason,
                filed.Id == item.Effective?.Id)).Reverse().ToList(),
            changes,
            Sources);
    }

    private static IReadOnlyList<SalesTaxLineChangeDto> Differences(SalesTaxReturn filed, Dr15Lines now)
    {
        Dr15Lines? then;
        try
        {
            then = JsonSerializer.Deserialize<SalesTaxWorksheetDto>(filed.SnapshotJson, SnapshotJson)?.Lines;
        }
        catch (JsonException)
        {
            then = null;
        }

        if (then is null) return [new SalesTaxLineChangeDto("Line 5", filed.TotalTaxDue, now.Line5)];

        var changes = new List<SalesTaxLineChangeDto>();
        void Compare(string line, decimal before, decimal after)
        {
            if (before != after) changes.Add(new SalesTaxLineChangeDto(line, before, after));
        }

        Compare("A1 Gross sales", then.LineA.Gross ?? 0m, now.LineA.Gross ?? 0m);
        Compare("A2 Exempt sales", then.LineA.Exempt ?? 0m, now.LineA.Exempt ?? 0m);
        Compare("A3 Taxable amount", then.LineA.Taxable, now.LineA.Taxable);
        Compare("A4 Tax due", then.LineA.TaxDue, now.LineA.TaxDue);
        Compare("B3 Taxable purchases", then.LineB.Taxable, now.LineB.Taxable);
        Compare("B4 Use tax due", then.LineB.TaxDue, now.LineB.TaxDue);
        Compare("Line 5", then.Line5, now.Line5);
        Compare("Line 6", then.Line6, now.Line6);
        Compare("Line 8", then.Line8, now.Line8);
        Compare("Line 9", then.Line9, now.Line9);
        Compare("Line 10", then.Line10, now.Line10);
        Compare("Line 15(a)", then.Line15a, now.Line15a);
        Compare("Line 15(b)", then.Line15b, now.Line15b);
        Compare("Line 15(c)", then.Line15c, now.Line15c);
        Compare("Line 15(d)", then.Line15d, now.Line15d);
        return changes;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    public static string Label(SalesTaxPeriod period)
    {
        CultureInfo us = CultureInfo.GetCultureInfo("en-US");
        string range = period.Start.Year == period.End.Year
            ? $"{period.Start.ToString("MMM", us)}–{period.End.ToString("MMM yyyy", us)}"
            : $"{period.Start.ToString("MMM yyyy", us)}–{period.End.ToString("MMM yyyy", us)}";
        if (period.IsFinal) return $"Final return {period.Start.ToString("MMM d", us)}–{period.End.ToString("MMM d, yyyy", us)}";

        return (period.Frequency, period.Alignment) switch
        {
            (SalesTaxFrequency.Monthly, _) => period.Start.ToString("MMMM yyyy", us),
            (SalesTaxFrequency.Quarterly, SalesTaxPeriodAlignment.Calendar) => $"Q{(period.Start.Month + 2) / 3} {period.Start.Year} ({range})",
            (SalesTaxFrequency.Annual, SalesTaxPeriodAlignment.Calendar) => $"{period.Start.Year}",
            _ => range,
        };
    }

    private static string? ValidateSchedule(IReadOnlyList<SalesTaxFrequencyDto>? frequencies, DateOnly registeredOn)
    {
        if (frequencies is null || frequencies.Count == 0) return "Add the filing frequency DOR assigned.";
        if (frequencies.Select(item => item.EffectiveFrom).Distinct().Count() != frequencies.Count) return "Two frequencies start on the same date.";

        List<SalesTaxFrequencyDto> ordered = frequencies.OrderBy(item => item.EffectiveFrom).ToList();
        if (ordered[0].EffectiveFrom > registeredOn) return "The first frequency must start on or before the registration date.";

        foreach (SalesTaxFrequencyDto item in ordered)
        {
            if (!Enum.IsDefined(item.Frequency) || !Enum.IsDefined(item.Alignment)) return "Choose a valid filing frequency.";
            if (item.Alignment == SalesTaxPeriodAlignment.Alternate && !FloridaFilingCalendar.SupportsAlternate(item.Frequency))
            {
                return "Alternate periods exist only for quarterly and semiannual filing.";
            }
        }

        foreach (SalesTaxFrequencyDto item in ordered.Skip(1))
        {
            if (!FloridaFilingCalendar.IsPeriodStart(item.EffectiveFrom, item.Frequency, item.Alignment))
            {
                return $"A {item.Frequency.ToString().ToLowerInvariant()} frequency must start on the first day of one of its periods.";
            }
        }

        return null;
    }

    private SalesTaxProfileDto ToProfile(SalesTaxRegistration? registration, string zone) => registration is null
        ? new SalesTaxProfileDto(false, Jurisdiction, null, null, null, null, null, null, null, null, null, null, null, null,
            FilesElectronically: true, DonateCollectionAllowance: false, DeliveryChargesOptional: true, null, [], zone, FloridaSurtax.Counties)
        : new SalesTaxProfileDto(
            registration.Frequencies.Count > 0,
            registration.Jurisdiction,
            registration.CertificateNumber,
            registration.BusinessPartnerNumber,
            registration.LegalName,
            registration.DoingBusinessAs,
            registration.AddressLine1,
            registration.AddressLine2,
            registration.City,
            registration.PostalCode,
            registration.County,
            FloridaSurtax.RateOn(registration.County, SalesTaxPeriodBuilder.LocalDate(clock.GetUtcNow(), Organization.ZoneOf(zone))),
            registration.RegisteredOn,
            registration.ClosedOn,
            registration.FilesElectronically,
            registration.DonateCollectionAllowance,
            registration.DeliveryChargesOptional,
            registration.Notes,
            registration.Schedule().Select(span => new SalesTaxFrequencyDto(span.EffectiveFrom, span.Frequency, span.Alignment)).ToList(),
            zone,
            FloridaSurtax.Counties);

    private async Task<string> TimeZoneIdAsync(CancellationToken cancellationToken) =>
        await store.FindTimeZoneIdAsync(currentUser.OrganizationId, cancellationToken) ?? Organization.DefaultTimeZoneId;

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken) =>
        SalesTaxPeriodBuilder.LocalDate(clock.GetUtcNow(), Organization.ZoneOf(await TimeZoneIdAsync(cancellationToken)));

    private static bool IsFloridaZone(string zone) => zone is "America/New_York" or "America/Chicago" or "US/Eastern" or "US/Central";

    private static bool IsZip(string zip) =>
        (zip.Length == 5 && zip.All(char.IsDigit)) || (zip.Length == 10 && zip[5] == '-' && zip.Remove(5, 1).All(char.IsDigit));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
