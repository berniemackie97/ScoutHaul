using HaulScout.Domain.Entities;
using HaulScout.Domain.Tax;

namespace HaulScout.Application.Contracts;

// Florida sales and use tax (DR-15).

public sealed record SalesTaxFrequencyDto(DateOnly EffectiveFrom, SalesTaxFrequency Frequency, SalesTaxPeriodAlignment Alignment);

public sealed record SalesTaxProfileDto(
    bool Configured,
    string Jurisdiction,
    string? CertificateNumber,
    string? BusinessPartnerNumber,
    string? LegalName,
    string? DoingBusinessAs,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? PostalCode,
    string? County,
    decimal? CountySurtaxRate,
    DateOnly? RegisteredOn,
    DateOnly? ClosedOn,
    bool FilesElectronically,
    bool DonateCollectionAllowance,
    bool DeliveryChargesOptional,
    string? Notes,
    IReadOnlyList<SalesTaxFrequencyDto> Frequencies,
    string TimeZoneId,
    IReadOnlyList<string> Counties);

public sealed record SaveSalesTaxProfileRequest(
    string? CertificateNumber,
    string? BusinessPartnerNumber,
    string LegalName,
    string? DoingBusinessAs,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? PostalCode,
    string County,
    DateOnly RegisteredOn,
    DateOnly? ClosedOn,
    bool FilesElectronically,
    bool DonateCollectionAllowance,
    bool DeliveryChargesOptional,
    string? Notes,
    IReadOnlyList<SalesTaxFrequencyDto> Frequencies);

public sealed record SalesTaxSchedulePreviewRequest(
    DateOnly? RegisteredOn,
    string? County,
    DateOnly? ClosedOn,
    IReadOnlyList<SalesTaxFrequencyDto>? Frequencies);

public sealed record SalesTaxPreviewPeriodDto(
    string Key,
    string Label,
    DateOnly Start,
    DateOnly End,
    DateOnly LateAfter,
    DateOnly ElectronicPaymentDeadline,
    SalesTaxPeriodStatus Status,
    int DaysUntilLateAfter);

/// <summary>What a registration would set up, before it is saved. Never an error: an incomplete form gets a Problem.</summary>
public sealed record SalesTaxSchedulePreviewDto(
    string? Problem,
    string? County,
    decimal? SurtaxRate,
    decimal? CombinedRate,
    DateOnly Today,
    IReadOnlyList<SalesTaxPreviewPeriodDto> Periods);

public enum SalesTaxPeriodStatus
{
    Open,
    ReadyToFile,
    DueSoon,
    Overdue,
    Filed,
    FiledLate,
    ChangedSinceFiled,
}

public sealed record SalesTaxFiledSummaryDto(
    Guid Id,
    SalesTaxReturnKind Kind,
    DateOnly FiledOn,
    DateOnly? PaidOn,
    SalesTaxFilingMethod Method,
    string? ConfirmationNumber,
    decimal AmountPaid,
    decimal AmountDueWithReturn,
    bool Late);

public sealed record SalesTaxPeriodSummaryDto(
    string Key,
    string Label,
    DateOnly Start,
    DateOnly End,
    SalesTaxFrequency Frequency,
    bool IsFinal,
    DateOnly DueDate,
    DateOnly LateAfter,
    DateOnly ElectronicPaymentDeadline,
    SalesTaxPeriodStatus Status,
    int DaysUntilLateAfter,
    int ReportedSalesCount,
    decimal ReportedGrossSales,
    int ExcludedSalesCount,
    decimal ExcludedGrossSales,
    decimal TotalTaxDue,
    decimal AmountDueWithReturn,
    int BlockingIssues,
    int Warnings,
    SalesTaxFiledSummaryDto? Filed);

public sealed record SalesTaxNoticeDto(Dr15IssueSeverity Severity, string Code, string Message, string? PeriodKey = null);

public sealed record SalesTaxOverviewDto(
    SalesTaxProfileDto Profile,
    DateOnly Today,
    SalesTaxPeriodSummaryDto? NextDue,
    int OverdueCount,
    SalesTaxFrequency? EligibleFrequency,
    decimal TaxRemittedLastFourQuarters,
    IReadOnlyList<SalesTaxPeriodSummaryDto> Periods,
    IReadOnlyList<SalesTaxNoticeDto> Notices);

public sealed record SalesTaxReturnHeaderDto(
    string? CertificateNumber,
    string LegalName,
    string? DoingBusinessAs,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? PostalCode,
    string County,
    decimal SurtaxRate,
    string FormVersion);

public sealed record SalesTaxTimelinessDto(
    DateOnly FiledOn,
    DateOnly PaidOn,
    bool Electronic,
    bool Projected,
    bool ReturnLate,
    bool PaymentLate,
    bool CollectionAllowanceEarned,
    int InterestDays,
    IReadOnlyList<FloridaInterest.Segment> InterestSegments);

public sealed record SalesTaxReportedSaleDto(
    Guid SaleId,
    string InvoiceKey,
    DateOnly Date,
    string Channel,
    string ItemTitle,
    string? OrderNumber,
    Guid? OrderId,
    decimal ItemAmount,
    decimal ShippingCharged,
    decimal TaxCharged,
    Dr15Destination Destination,
    string? County,
    string DestinationSource,
    Dr15Exemption Exemption,
    bool HasDecision);

public sealed record SalesTaxInvoiceDto(
    string InvoiceKey,
    DateOnly Date,
    IReadOnlyList<Guid> SaleIds,
    decimal Gross,
    decimal Exempt,
    decimal Taxable,
    string? County,
    decimal SurtaxRate,
    decimal ComputedTax,
    decimal SurtaxPortion,
    decimal TaxCharged,
    decimal TaxDue,
    decimal OverItemCap,
    Dr15SurtaxClass SurtaxClass,
    string Treatment);

public sealed record SalesTaxExcludedChannelDto(string Channel, int SalesCount, decimal GrossSales, decimal TaxCollectedByMarketplace, string Basis);

public sealed record SalesTaxExcludedSaleDto(
    Guid SaleId,
    DateOnly Date,
    string Channel,
    string ItemTitle,
    decimal ItemAmount,
    decimal ShippingCharged,
    decimal TaxCollectedByMarketplace);

public sealed record SalesTaxUseEntryDto(
    Guid Id,
    DateOnly OccurredOn,
    string Description,
    decimal Amount,
    string? County,
    decimal OtherStateTaxPaid,
    Guid? ExpenseId,
    Guid? InventoryItemId,
    string? Notes);

public sealed record SalesTaxUseLineDto(SalesTaxUseEntryDto Entry, Dr15UseTaxResult Result);

public sealed record SalesTaxRefundLineDto(Guid RefundId, Guid SaleId, DateOnly Date, string Channel, decimal TaxRefunded);

public sealed record SalesTaxAdjustmentDto(Guid Id, SalesTaxAdjustmentKind Kind, decimal Amount, string Description);

public sealed record SalesTaxCarryDto(decimal DeductionsCarriedIn, decimal CreditsCarriedIn, decimal DeductionsCarriedOut, decimal CreditsCarriedOut);

public sealed record SalesTaxIssueDto(Dr15IssueSeverity Severity, string Code, string Message, string? Reference, IReadOnlyList<Guid> SaleIds);

public sealed record SalesTaxLineChangeDto(string Line, decimal Filed, decimal Now);

public sealed record SalesTaxFiledReturnDto(
    Guid Id,
    SalesTaxReturnKind Kind,
    Guid? SupersedesReturnId,
    DateOnly FiledOn,
    DateOnly? PaidOn,
    SalesTaxFilingMethod Method,
    string? ConfirmationNumber,
    decimal AmountPaid,
    bool DonatedCollectionAllowance,
    decimal TotalTaxDue,
    decimal AmountDueWithReturn,
    int WarningsAcknowledged,
    bool AttestedDirectSalesRecorded,
    bool AttestedUseTaxReviewed,
    string? Notes,
    DateTimeOffset RecordedAt,
    bool Voided,
    string? VoidReason,
    bool IsEffective);

public sealed record SalesTaxWorksheetDto(
    SalesTaxPeriodSummaryDto Period,
    SalesTaxReturnHeaderDto Header,
    Dr15Lines Lines,
    SalesTaxTimelinessDto Timeliness,
    IReadOnlyList<SalesTaxInvoiceDto> Invoices,
    IReadOnlyList<SalesTaxReportedSaleDto> ReportedSales,
    IReadOnlyList<SalesTaxExcludedChannelDto> ExcludedChannels,
    IReadOnlyList<SalesTaxExcludedSaleDto> ExcludedSales,
    IReadOnlyList<SalesTaxUseLineDto> UseTax,
    IReadOnlyList<SalesTaxRefundLineDto> Refunds,
    IReadOnlyList<SalesTaxAdjustmentDto> Adjustments,
    SalesTaxCarryDto Carry,
    IReadOnlyList<SalesTaxIssueDto> Issues,
    string SourceFingerprint,
    IReadOnlyList<SalesTaxFiledReturnDto> Filings,
    IReadOnlyList<SalesTaxLineChangeDto> ChangesSinceFiled,
    IReadOnlyList<string> Sources);

public sealed record FileSalesTaxReturnRequest(
    DateOnly FiledOn,
    DateOnly? PaidOn,
    SalesTaxFilingMethod Method,
    string? ConfirmationNumber,
    decimal AmountPaid,
    bool DonateCollectionAllowance,
    bool Amended,
    int AcknowledgedWarnings,
    string ExpectedFingerprint,
    string? Notes,
    bool ConfirmedDirectSalesRecorded = false,
    bool ConfirmedUseTaxReviewed = false);

public sealed record VoidSalesTaxReturnRequest(string Reason);

public sealed record SaveSalesTaxUseEntryRequest(
    DateOnly OccurredOn,
    string Description,
    decimal Amount,
    string? County,
    decimal OtherStateTaxPaid,
    Guid? ExpenseId,
    Guid? InventoryItemId,
    string? Notes);

public sealed record SaveSalesTaxAdjustmentRequest(string PeriodKey, SalesTaxAdjustmentKind Kind, decimal Amount, string Description);

public sealed record SaveSalesTaxSaleDecisionRequest(
    Dr15Destination Destination,
    string? County,
    Dr15Exemption Exemption,
    string? ExemptionCertificate,
    string? Notes);

public sealed record SetSaleTaxCollectorRequest(bool MarketplaceCollected, string Reason);
