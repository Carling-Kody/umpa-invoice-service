namespace UmpaInvoiceService.Intacct.Xml;

/// <summary>Parsed response: the set of function results, addressable by control id.</summary>
public sealed class IntacctResponse
{
    public required IReadOnlyList<IntacctResult> Results { get; init; }

    /// <summary>Gets the result for a function's control id.</summary>
    public IntacctResult this[string controlId] =>
        Results.FirstOrDefault(r => r.ControlId == controlId)
        ?? throw new KeyNotFoundException($"No result for control id '{controlId}'.");
}
