using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Xml;

/// <summary>One &lt;result&gt; from a response, correlated to a request function by control id.</summary>
public sealed class IntacctResult
{
    public required string ControlId { get; init; }
    public required string Function { get; init; }
    public required bool Success { get; init; }

    public IReadOnlyList<IntacctError> Errors { get; init; } = Array.Empty<IntacctError>();

    /// <summary>The raw &lt;data&gt; element, or null if the function returned none.</summary>
    public XElement? Data { get; init; }

    /// <summary>Total records matching the query, across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Records still unread after this page.</summary>
    public int NumRemaining { get; init; }

    /// <summary>
    /// Opaque id of the server-side result set, passed to readMore to fetch the next page.
    /// Null when the function did not open one (or every record fit on a single page).
    /// </summary>
    public string? ResultId { get; init; }

    /// <summary>Key returned by create/update functions (e.g. the new record's id).</summary>
    public string? Key { get; init; }
}
