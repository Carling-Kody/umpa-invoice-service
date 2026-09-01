namespace UmpaInvoiceService.Intacct;

/// <summary>
/// Result of getAPISession: a session id plus the endpoint to use for the rest of the session.
/// Reusing the session id (rather than re-sending login credentials on every call) is Sage's
/// recommended practice.
/// </summary>
public sealed record IntacctSession(string SessionId, string Endpoint);
