using System.Globalization;
using System.Text;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Tax;

namespace HaulScout.Application.Tax;

/// <summary>The worksheet's transaction schedule as CSV: what fed each line, for the records DR-15N says to keep three years.</summary>
public static class SalesTaxCsv
{
    public static string Build(SalesTaxWorksheetDto worksheet)
    {
        var csv = new StringBuilder();
        void Row(params object?[] cells) => csv.AppendLine(string.Join(',', cells.Select(Cell)));

        Dr15Lines lines = worksheet.Lines;
        Row("Florida DR-15 worksheet", worksheet.Header.FormVersion);
        Row("Business", worksheet.Header.LegalName);
        Row("Certificate", worksheet.Header.CertificateNumber);
        Row("Period", worksheet.Period.Label, worksheet.Period.Start, worksheet.Period.End);
        Row("Late after", worksheet.Period.LateAfter);
        Row("Status", worksheet.Period.Status);
        Row("Source fingerprint", worksheet.SourceFingerprint);
        csv.AppendLine();

        Row("Line", "Gross sales", "Exempt sales", "Taxable amount", "Tax due");
        Row("A Sales/Services", lines.LineA.Gross, lines.LineA.Exempt, lines.LineA.Taxable, lines.LineA.TaxDue);
        Row("B Taxable purchases", null, null, lines.LineB.Taxable, lines.LineB.TaxDue);
        Row("C Commercial rentals", 0m, 0m, 0m, 0m);
        Row("D Transient rentals", 0m, 0m, 0m, 0m);
        Row("E Food & beverage vending", 0m, 0m, 0m, 0m);
        foreach ((string label, decimal value) in new (string, decimal)[]
                 {
                     ("5 Total amount of tax due", lines.Line5), ("6 Less lawful deductions", lines.Line6), ("7 Net tax due", lines.Line7),
                     ("8 Less est tax pd / DOR cr memo", lines.Line8), ("9 Plus est tax due current month", lines.Line9), ("10 Amount due", lines.Line10),
                     ("11 Less collection allowance", lines.Line11), ("12 Plus penalty", lines.Line12), ("13 Plus interest", lines.Line13),
                     ("14 Amount due with return", lines.Line14), ("15(a) Exempt amount of items over $5,000", lines.Line15a),
                     ("15(b) Other taxable amounts not subject to surtax", lines.Line15b),
                     ("15(c) Amounts subject to surtax at a different rate", lines.Line15c),
                     ("15(d) Total discretionary sales surtax due", lines.Line15d),
                 })
        {
            Row(label, value);
        }

        csv.AppendLine();
        Row("Reported sales");
        Row("Sale id", "Invoice", "Date", "Channel", "Item", "Order", "Item amount", "Shipping charged", "Tax charged", "Destination", "County", "Destination source", "Exemption");
        foreach (SalesTaxReportedSaleDto sale in worksheet.ReportedSales)
        {
            Row(sale.SaleId, sale.InvoiceKey, sale.Date, sale.Channel, sale.ItemTitle, sale.OrderNumber, sale.ItemAmount, sale.ShippingCharged,
                sale.TaxCharged, sale.Destination, sale.County, sale.DestinationSource, sale.Exemption);
        }

        csv.AppendLine();
        Row("Invoices");
        Row("Invoice", "Date", "Gross", "Exempt", "Taxable", "County", "Surtax rate", "Computed tax", "Surtax portion", "Tax charged", "Tax due", "Over $5,000", "Treatment");
        foreach (SalesTaxInvoiceDto invoice in worksheet.Invoices)
        {
            Row(invoice.InvoiceKey, invoice.Date, invoice.Gross, invoice.Exempt, invoice.Taxable, invoice.County, invoice.SurtaxRate, invoice.ComputedTax,
                invoice.SurtaxPortion, invoice.TaxCharged, invoice.TaxDue, invoice.OverItemCap, invoice.Treatment);
        }

        csv.AppendLine();
        Row("Use tax (Line B)");
        Row("Entry id", "Date", "Description", "Amount", "County", "Surtax rate", "Tax", "Other-state credit");
        foreach (SalesTaxUseLineDto use in worksheet.UseTax)
        {
            Row(use.Entry.Id, use.Entry.OccurredOn, use.Entry.Description, use.Result.Taxable, use.Result.County, use.Result.SurtaxRate, use.Result.Tax, use.Result.OtherStateCredit);
        }

        csv.AppendLine();
        Row("Tax refunded to buyers (Line 6)");
        Row("Refund id", "Sale id", "Date", "Channel", "Tax refunded");
        foreach (SalesTaxRefundLineDto refund in worksheet.Refunds)
        {
            Row(refund.RefundId, refund.SaleId, refund.Date, refund.Channel, refund.TaxRefunded);
        }

        csv.AppendLine();
        Row("Excluded: marketplace provider collected (s. 212.05965(4)(a), F.S.)");
        Row("Sale id", "Date", "Channel", "Item", "Item amount", "Shipping charged", "Tax collected by marketplace");
        foreach (SalesTaxExcludedSaleDto sale in worksheet.ExcludedSales)
        {
            Row(sale.SaleId, sale.Date, sale.Channel, sale.ItemTitle, sale.ItemAmount, sale.ShippingCharged, sale.TaxCollectedByMarketplace);
        }

        return csv.ToString();
    }

    private static string Cell(object? value) => value switch
    {
        null => "",
        decimal number => number.ToString("0.00####", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
    };

    private static string Text(string value)
    {
        // A leading =, +, - or @ would run as a formula in a spreadsheet.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
