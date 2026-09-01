using System.Runtime.CompilerServices;
using System.Xml.Linq;
using UmpaInvoiceService.Intacct.Functions;

namespace UmpaInvoiceService.Intacct.Services;

/// <summary>
/// Paged reads, for pulling Intacct data into an external store.
///
/// readByQuery returns at most one page (see <see cref="ReadByQueryFunction.PageSize"/>, max 1000)
/// and parks the remainder in a server-side result set; readMore walks it. Because that result set
/// belongs to the session that opened it, a single enumeration must run against one
/// <see cref="IntacctClient"/> — don't split it across clients or interleave two enumerations of
/// different objects on separate clients expecting them to share state.
/// </summary>
public sealed class IntacctReadService
{
    private readonly IntacctClient _client;

    public IntacctReadService(IntacctClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// Streams every record matching the query, transparently following readMore across pages.
    /// Records are yielded as their raw XML elements (e.g. &lt;arinvoice&gt;), one per record.
    /// </summary>
    public async IAsyncEnumerable<XElement> ReadAllAsync(
        ReadByQueryFunction query,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var response = await _client.ExecuteAsync(query, ct).ConfigureAwait(false);
        var page = response[query.ControlId];
        if (!page.Success)
            throw new IntacctException($"readByQuery on {query.ObjectName} failed.", page.Errors);

        foreach (var record in Records(page))
            yield return record;

        var remaining = page.NumRemaining;
        var resultId = page.ResultId;
        var fetched = Records(page).Count();

        while (remaining > 0 && !string.IsNullOrWhiteSpace(resultId))
        {
            ct.ThrowIfCancellationRequested();

            var more = new ReadMoreFunction(resultId);
            var moreResponse = await _client.ExecuteAsync(more, ct).ConfigureAwait(false);
            var next = moreResponse[more.ControlId];
            if (!next.Success)
                throw new IntacctException(
                    $"readMore on {query.ObjectName} failed after {fetched} of {page.TotalCount} records.",
                    next.Errors);

            var batch = Records(next).ToList();
            foreach (var record in batch)
                yield return record;

            fetched += batch.Count;

            // Every page must consume some of the remainder. If it doesn't, the result set is not
            // advancing and looping again would hammer the API forever — fail loudly instead.
            if (batch.Count == 0 || next.NumRemaining >= remaining)
                throw new IntacctException(
                    $"readMore on {query.ObjectName} stopped advancing at {fetched} of {page.TotalCount} " +
                    $"records (returned {batch.Count} records, {next.NumRemaining} still remaining).");

            remaining = next.NumRemaining;
        }
    }

    /// <summary>
    /// Convenience wrapper over <see cref="ReadAllAsync"/> that buffers every record into a list.
    /// Prefer the streaming overload for large objects.
    /// </summary>
    public async Task<List<XElement>> ReadAllToListAsync(
        ReadByQueryFunction query,
        CancellationToken ct = default)
    {
        var all = new List<XElement>();
        await foreach (var record in ReadAllAsync(query, ct).ConfigureAwait(false))
            all.Add(record);
        return all;
    }

    private static IEnumerable<XElement> Records(Xml.IntacctResult result) =>
        result.Data?.Elements() ?? Enumerable.Empty<XElement>();
}
