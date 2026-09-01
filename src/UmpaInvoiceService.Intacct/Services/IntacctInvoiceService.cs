using UmpaInvoiceService.Intacct.Functions;

namespace UmpaInvoiceService.Intacct.Services;

/// <summary>High-level operations for posting invoice data into Sage Intacct.</summary>
public sealed class IntacctInvoiceService
{
    private readonly IntacctClient _client;

    public IntacctInvoiceService(IntacctClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Posts an AR invoice and returns the created record's key.</summary>
    public async Task<string> CreateInvoiceAsync(CreateArInvoiceFunction invoice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var response = await _client.ExecuteAsync(invoice, ct).ConfigureAwait(false);
        var result = response[invoice.ControlId];

        if (!result.Success)
            throw new IntacctException("Failed to create AR invoice.", result.Errors);

        return result.Key
            ?? throw new IntacctException("AR invoice was created but Intacct returned no key.");
    }
}
