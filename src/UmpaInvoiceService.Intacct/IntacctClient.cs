using System.Text;
using System.Xml.Linq;
using UmpaInvoiceService.Intacct.Functions;
using UmpaInvoiceService.Intacct.Xml;

namespace UmpaInvoiceService.Intacct;

/// <summary>
/// Session-based client for the Sage Intacct XML Web Services gateway.
///
/// The first call transparently runs getAPISession (login auth) to obtain a session id and a
/// per-session endpoint; every subsequent call reuses that session id. Create one client per
/// company connection and reuse it; the class is safe for concurrent use.
/// </summary>
public sealed class IntacctClient : IDisposable
{
    private readonly IntacctClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private IntacctSession? _session;

    public IntacctClient(IntacctClientOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = httpClient ?? new HttpClient();
        _ownsHttp = httpClient is null;
    }

    /// <summary>The active session once established, otherwise null.</summary>
    public IntacctSession? Session => _session;

    /// <summary>Ensures a session exists, running getAPISession once if needed.</summary>
    public async Task<IntacctSession> EnsureSessionAsync(CancellationToken ct = default)
    {
        if (_session is not null) return _session;

        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is not null) return _session;

            var fn = new ApiSessionFunction { EntityId = _options.EntityId };
            // No session yet: login auth against the configured (default) endpoint.
            var response = await SendAsync(session: null, _options.Endpoint, new[] { fn }, ct)
                .ConfigureAwait(false);

            var result = response[fn.ControlId];
            if (!result.Success)
                throw new IntacctException("getAPISession failed.", result.Errors);

            var api = result.Data?.Element("api")
                ?? throw new IntacctException("getAPISession returned no <api> data.");
            var sessionId = (string?)api.Element("sessionid")
                ?? throw new IntacctException("getAPISession returned no session id.");
            var endpoint = (string?)api.Element("endpoint");

            _session = new IntacctSession(
                sessionId,
                string.IsNullOrWhiteSpace(endpoint) ? _options.Endpoint : endpoint);
            return _session;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>Executes one or more functions in a single request, auto-starting a session if needed.</summary>
    public async Task<IntacctResponse> ExecuteAsync(
        IReadOnlyList<IIntacctFunction> functions,
        CancellationToken ct = default)
    {
        if (functions is null || functions.Count == 0)
            throw new ArgumentException("At least one function is required.", nameof(functions));

        var session = await EnsureSessionAsync(ct).ConfigureAwait(false);
        return await SendAsync(session, session.Endpoint, functions, ct).ConfigureAwait(false);
    }

    /// <summary>Convenience overload for a single function.</summary>
    public Task<IntacctResponse> ExecuteAsync(IIntacctFunction function, CancellationToken ct = default)
        => ExecuteAsync(new[] { function }, ct);

    private async Task<IntacctResponse> SendAsync(
        IntacctSession? session,
        string endpoint,
        IReadOnlyList<IIntacctFunction> functions,
        CancellationToken ct)
    {
        var requestXml = IntacctRequestBuilder
            .Build(_options, session, functions)
            .ToString(SaveOptions.DisableFormatting);

        using var content = new StringContent(requestXml, Encoding.UTF8, "application/xml");
        using var httpResponse = await _http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var body = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        try
        {
            // The XML gateway returns a structured <response> for most errors, frequently with a
            // non-2xx status (e.g. GW-0011 for an invalid sender). Parse it so control-, auth-, and
            // function-level failures surface as structured IntacctError entries rather than raw XML.
            return IntacctResponseParser.Parse(body);
        }
        catch (IntacctException) when (!httpResponse.IsSuccessStatusCode && !LooksLikeIntacctResponse(body))
        {
            // Body isn't a recognizable Intacct response (e.g. an HTML 5xx page) — surface transport error.
            throw new IntacctException(
                $"Intacct gateway returned HTTP {(int)httpResponse.StatusCode}. Body: {Truncate(body)}");
        }
    }

    private static bool LooksLikeIntacctResponse(string body) =>
        body.Contains("<response", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";

    public void Dispose()
    {
        _sessionLock.Dispose();
        if (_ownsHttp) _http.Dispose();
    }
}
