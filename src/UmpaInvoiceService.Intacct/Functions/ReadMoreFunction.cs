using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Functions;

/// <summary>
/// readMore — fetches the next page of a result set opened by <see cref="ReadByQueryFunction"/>.
///
/// The result set lives on the session that created it, so every readMore must go through the
/// same <see cref="IntacctClient"/> as the original readByQuery.
/// </summary>
public sealed class ReadMoreFunction : IIntacctFunction
{
    public string ControlId { get; } = Guid.NewGuid().ToString();

    /// <summary>Opaque result-set id returned on the previous page's &lt;data&gt; element.</summary>
    public string ResultId { get; }

    public ReadMoreFunction(string resultId)
    {
        if (string.IsNullOrWhiteSpace(resultId))
            throw new ArgumentException("A result id is required.", nameof(resultId));
        ResultId = resultId;
    }

    public XElement ToXml() =>
        new XElement("function",
            new XAttribute("controlid", ControlId),
            new XElement("readMore",
                new XElement("resultId", ResultId)));
}
