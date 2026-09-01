using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Functions;

/// <summary>
/// readByQuery — reads records of one object matching a SQL-like query.
/// <example>
/// new ReadByQueryFunction("ARINVOICE")
/// {
///     Fields = "RECORDNO,RECORDID,CUSTOMERID,TOTALDUE,STATE",
///     Query = "STATE = 'Posted'",
///     PageSize = 100,
/// };
/// </example>
/// </summary>
public sealed class ReadByQueryFunction : IIntacctFunction
{
    public string ControlId { get; } = Guid.NewGuid().ToString();

    /// <summary>Integration/object name, e.g. "ARINVOICE", "CUSTOMER", "GLACCOUNT".</summary>
    public string ObjectName { get; }

    /// <summary>Filter expression. Empty returns all records (paged).</summary>
    public string? Query { get; init; }

    /// <summary>Comma-separated field list, or "*" for all readable fields.</summary>
    public string Fields { get; init; } = "*";

    /// <summary>Records per page (max 1000).</summary>
    public int PageSize { get; init; } = 100;

    public ReadByQueryFunction(string objectName) => ObjectName = objectName;

    public XElement ToXml() =>
        new XElement("function",
            new XAttribute("controlid", ControlId),
            new XElement("readByQuery",
                new XElement("object", ObjectName),
                new XElement("query", Query ?? string.Empty),
                new XElement("fields", Fields),
                new XElement("pagesize", PageSize)));
}
