namespace UmpaInvoiceService.Intacct;

/// <summary>Raised when Intacct rejects a request or the client cannot interpret the response.</summary>
public sealed class IntacctException : Exception
{
    public IReadOnlyList<IntacctError> Errors { get; }

    public IntacctException(string message, IReadOnlyList<IntacctError>? errors = null)
        : base(BuildMessage(message, errors))
    {
        Errors = errors ?? Array.Empty<IntacctError>();
    }

    private static string BuildMessage(string message, IReadOnlyList<IntacctError>? errors)
    {
        if (errors is null || errors.Count == 0) return message;
        return message + " " + string.Join("; ", errors);
    }
}

/// <summary>A single Intacct &lt;error&gt; entry from an errormessage block.</summary>
public sealed record IntacctError(string? Number, string? Description, string? Description2, string? Correction)
{
    public override string ToString()
    {
        var parts = new[] { Number, Description, Description2, Correction }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" | ", parts);
    }
}
