using HaulScout.Domain.Common;
using HaulScout.Domain.Tax;

namespace HaulScout.Domain.Entities;

/// <summary>The organization's sales tax registration with one state: the facts printed on its returns.</summary>
public sealed class SalesTaxRegistration : EntityBase, IOrganizationScoped
{
    public const string Florida = "FL";

    public Guid OrganizationId { get; set; }
    public string Jurisdiction { get; set; } = Florida;
    public string? CertificateNumber { get; set; }
    public string? BusinessPartnerNumber { get; set; }
    public required string LegalName { get; set; }
    public string? DoingBusinessAs { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Canonical county name from <see cref="FloridaSurtax"/>; sets the surtax rate printed on the return.</summary>
    public required string County { get; set; }

    public DateOnly RegisteredOn { get; set; }
    public DateOnly? ClosedOn { get; set; }

    /// <summary>Files and pays through eServices — the only way to earn the collection allowance.</summary>
    public bool FilesElectronically { get; set; } = true;

    public bool DonateCollectionAllowance { get; set; }

    /// <summary>Shipping is separately stated and buyers may pick up instead, so it is not taxable (Rule 12A-1.045).</summary>
    public bool DeliveryChargesOptional { get; set; } = true;

    public string? Notes { get; set; }

    public ICollection<SalesTaxFrequencyChange> Frequencies { get; set; } = [];

    public IReadOnlyList<SalesTaxFrequencySpan> Schedule() =>
        Frequencies.OrderBy(change => change.EffectiveFrom)
            .Select(change => new SalesTaxFrequencySpan(change.EffectiveFrom, change.Frequency, change.Alignment))
            .ToList();
}

public sealed class SalesTaxFrequencyChange : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid RegistrationId { get; set; }
    public SalesTaxRegistration? Registration { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public SalesTaxFrequency Frequency { get; set; }
    public SalesTaxPeriodAlignment Alignment { get; set; }
}

public enum SalesTaxReturnKind
{
    Original,
    AmendedReplacement,
}

public enum SalesTaxFilingMethod
{
    Electronic,
    Paper,
    Telefile,
}

/// <summary>
/// A return as filed: the full worksheet snapshot and a fingerprint of every source row behind it. Never edited;
/// a correction is an amended replacement that supersedes it, and a mistaken record is voided, not deleted.
/// </summary>
public sealed class SalesTaxReturn : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid RegistrationId { get; set; }
    public string Jurisdiction { get; set; } = SalesTaxRegistration.Florida;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public SalesTaxFrequency Frequency { get; set; }
    public bool IsFinal { get; set; }
    public SalesTaxReturnKind Kind { get; set; }
    public Guid? SupersedesReturnId { get; set; }

    public DateOnly FiledOn { get; set; }
    public DateOnly? PaidOn { get; set; }
    public SalesTaxFilingMethod Method { get; set; }
    public string? ConfirmationNumber { get; set; }
    public decimal AmountPaid { get; set; }
    public bool DonatedCollectionAllowance { get; set; }

    public decimal GrossSales { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TotalTaxDue { get; set; }
    public decimal AmountDue { get; set; }
    public decimal AmountDueWithReturn { get; set; }
    public decimal DeductionsCarriedOut { get; set; }
    public decimal CreditsCarriedOut { get; set; }

    public int WarningsAcknowledged { get; set; }

    /// <summary>The filer confirmed every sale made outside a marketplace is recorded; the app cannot see unrecorded ones.</summary>
    public bool AttestedDirectSalesRecorded { get; set; }

    /// <summary>The filer confirmed untaxed business purchases were entered as use tax.</summary>
    public bool AttestedUseTaxReviewed { get; set; }
    public required string SourceFingerprint { get; set; }
    public required string SnapshotJson { get; set; }
    public string? Notes { get; set; }
    public Guid FiledByUserId { get; set; }

    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public Guid? VoidedByUserId { get; set; }

    public bool IsVoided => VoidedAt is not null;
}

/// <summary>A Line B use-tax item: a taxable purchase the business used without paying Florida tax to the seller.</summary>
public sealed class SalesTaxUseEntry : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Where it was used; null means the business county.</summary>
    public string? County { get; set; }

    /// <summary>Sales tax legally paid to another state on the purchase, credited on Line 6.</summary>
    public decimal OtherStateTaxPaid { get; set; }

    public Guid? ExpenseId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public string? Notes { get; set; }
    public Guid RecordedByUserId { get; set; }
}

/// <summary>An operator's answer for one reportable sale: where it was delivered and whether it was exempt.</summary>
public sealed class SalesTaxSaleDecision : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid SaleId { get; set; }
    public Dr15Destination Destination { get; set; }
    public string? County { get; set; }
    public Dr15Exemption Exemption { get; set; }

    /// <summary>Resale certificate or DR-14 number backing an exemption.</summary>
    public string? ExemptionCertificate { get; set; }

    public string? Notes { get; set; }
    public Guid DecidedByUserId { get; set; }
}

public enum SalesTaxAdjustmentKind
{
    /// <summary>Line 6: a deduction allowed by law other than tax refunded on recorded refunds.</summary>
    LawfulDeduction,

    /// <summary>Line 8: estimated tax paid last month, a DOR credit memo, or an approved credit.</summary>
    CreditOrEstimatedTaxPaid,

    /// <summary>Line 9: estimated tax due this month (dealers over $200,000 a year only).</summary>
    EstimatedTaxDue,
}

/// <summary>A manual amount for one period's Lines 6, 8 or 9, kept so the return can be rebuilt exactly.</summary>
public sealed class SalesTaxAdjustment : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public SalesTaxAdjustmentKind Kind { get; set; }
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public Guid RecordedByUserId { get; set; }
}
