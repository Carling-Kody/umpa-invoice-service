namespace UmpaInvoiceService.Intacct;

/// <summary>
/// Connection settings for the Sage Intacct XML Web Services gateway.
///
/// Two credential layers are required:
///  - <b>Sender</b> credentials (<see cref="SenderId"/>/<see cref="SenderPassword"/>) identify your
///    application. They come from a Sage Web Services developer license.
///  - <b>Login</b> credentials (<see cref="CompanyId"/>/<see cref="UserId"/>/<see cref="UserPassword"/>)
///    authenticate into a specific Intacct company. Use a dedicated Web Services user, and make sure
///    the target company has authorized your <see cref="SenderId"/> in its Web Services authorizations.
/// </summary>
public sealed class IntacctClientOptions
{
    /// <summary>
    /// Default Intacct XML gateway. getAPISession returns a per-session endpoint that the client
    /// uses for the remainder of the session.
    /// </summary>
    public const string DefaultEndpoint = "https://api.intacct.com/ia/xml/xmlgw.phtml";

    // --- Sender (application) credentials, from your Web Services developer license ---
    public required string SenderId { get; init; }
    public required string SenderPassword { get; init; }

    // --- Login (company) credentials, from a Web Services user in the target company ---
    public required string CompanyId { get; init; }
    public required string UserId { get; init; }
    public required string UserPassword { get; init; }

    /// <summary>Optional entity/location id for multi-entity (top-level vs. entity-level) access.</summary>
    public string? EntityId { get; init; }

    /// <summary>Gateway endpoint for the initial getAPISession call. Overridden per-session afterward.</summary>
    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>DTD version for the XML gateway. 3.0 is current.</summary>
    public string DtdVersion { get; init; } = "3.0";
}
