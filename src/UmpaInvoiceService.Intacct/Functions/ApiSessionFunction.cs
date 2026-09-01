using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Functions;

/// <summary>getAPISession — exchanges login credentials for a session id and a per-session endpoint.</summary>
public sealed class ApiSessionFunction : IIntacctFunction
{
    public string ControlId { get; } = Guid.NewGuid().ToString();

    /// <summary>Optional entity/location to scope the session to.</summary>
    public string? EntityId { get; init; }

    public XElement ToXml()
    {
        var session = new XElement("getAPISession");
        if (!string.IsNullOrWhiteSpace(EntityId))
            session.Add(new XElement("locationid", EntityId));

        return new XElement("function",
            new XAttribute("controlid", ControlId),
            session);
    }
}
