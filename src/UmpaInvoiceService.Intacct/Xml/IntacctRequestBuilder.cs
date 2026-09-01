using System.Xml.Linq;
using UmpaInvoiceService.Intacct.Functions;

namespace UmpaInvoiceService.Intacct.Xml;

/// <summary>Builds the Intacct request envelope: &lt;request&gt;(&lt;control&gt;, &lt;operation&gt;).</summary>
internal static class IntacctRequestBuilder
{
    public static XDocument Build(
        IntacctClientOptions options,
        IntacctSession? session,
        IReadOnlyList<IIntacctFunction> functions)
    {
        var control = new XElement("control",
            new XElement("senderid", options.SenderId),
            new XElement("password", options.SenderPassword),
            new XElement("controlid", Guid.NewGuid().ToString()),
            new XElement("uniqueid", "false"),
            new XElement("dtdversion", options.DtdVersion),
            new XElement("includewhitespace", "false"));

        // Once we hold a session id, authenticate with it; otherwise fall back to login auth
        // (used only for the initial getAPISession call).
        XElement authentication = session is not null
            ? new XElement("authentication",
                new XElement("sessionid", session.SessionId))
            : new XElement("authentication",
                new XElement("login",
                    new XElement("userid", options.UserId),
                    new XElement("companyid", options.CompanyId),
                    new XElement("password", options.UserPassword),
                    string.IsNullOrWhiteSpace(options.EntityId)
                        ? null
                        : new XElement("locationid", options.EntityId)));

        var content = new XElement("content", functions.Select(f => f.ToXml()));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("request",
                control,
                new XElement("operation",
                    authentication,
                    content)));
    }
}
