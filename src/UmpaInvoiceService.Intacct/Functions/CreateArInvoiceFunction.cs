using System.Globalization;
using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Functions;

/// <summary>
/// create_invoice — posts an accounts-receivable (AR) invoice.
///
/// This is a minimal, commonly-used field set. Intacct's AR invoice object supports many more
/// optional fields (terms, currency, dimensions, custom fields). Add them here as your service needs.
/// See https://developer.intacct.com/api/accounts-receivable/invoices/
/// </summary>
public sealed class CreateArInvoiceFunction : IIntacctFunction
{
    public string ControlId { get; } = Guid.NewGuid().ToString();

    /// <summary>Customer id the invoice is billed to.</summary>
    public required string CustomerId { get; init; }

    /// <summary>Invoice (GL posting) date.</summary>
    public required DateOnly InvoiceDate { get; init; }

    /// <summary>Optional due date. If omitted, Intacct derives it from the customer's terms.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>Optional external invoice number. If omitted, Intacct may auto-number.</summary>
    public string? InvoiceNumber { get; init; }

    /// <summary>Optional invoice-level description.</summary>
    public string? Description { get; init; }

    /// <summary>At least one line is required.</summary>
    public required IReadOnlyList<ArInvoiceLine> Lines { get; init; }

    public XElement ToXml()
    {
        if (Lines is null || Lines.Count == 0)
            throw new InvalidOperationException("An AR invoice requires at least one line item.");

        var invoice = new XElement("invoice",
            new XElement("customerid", CustomerId),
            DateElement("datecreated", InvoiceDate));

        if (DueDate is { } due)
            invoice.Add(DateElement("datedue", due));
        if (!string.IsNullOrWhiteSpace(InvoiceNumber))
            invoice.Add(new XElement("invoiceno", InvoiceNumber));
        if (!string.IsNullOrWhiteSpace(Description))
            invoice.Add(new XElement("description", Description));

        var items = new XElement("invoiceitems");
        foreach (var line in Lines)
        {
            var item = new XElement("lineitem",
                new XElement("glaccountno", line.GlAccountNo),
                new XElement("amount", line.Amount.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(line.Memo))
                item.Add(new XElement("memo", line.Memo));
            if (!string.IsNullOrWhiteSpace(line.DepartmentId))
                item.Add(new XElement("departmentid", line.DepartmentId));
            if (!string.IsNullOrWhiteSpace(line.LocationId))
                item.Add(new XElement("locationid", line.LocationId));
            items.Add(item);
        }
        invoice.Add(items);

        return new XElement("function",
            new XAttribute("controlid", ControlId),
            new XElement("create_invoice", invoice));
    }

    // Legacy create_invoice expects structured dates: <datecreated><year/><month/><day/></datecreated>
    private static XElement DateElement(string name, DateOnly d) =>
        new XElement(name,
            new XElement("year", d.Year.ToString("D4", CultureInfo.InvariantCulture)),
            new XElement("month", d.Month.ToString("D2", CultureInfo.InvariantCulture)),
            new XElement("day", d.Day.ToString("D2", CultureInfo.InvariantCulture)));
}

/// <summary>One line of an AR invoice.</summary>
public sealed record ArInvoiceLine
{
    /// <summary>GL account number to credit for this line.</summary>
    public required string GlAccountNo { get; init; }

    /// <summary>Line amount.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Optional line memo.</summary>
    public string? Memo { get; init; }

    /// <summary>Optional department dimension.</summary>
    public string? DepartmentId { get; init; }

    /// <summary>Optional location dimension.</summary>
    public string? LocationId { get; init; }
}
