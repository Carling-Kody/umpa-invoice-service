using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Functions;

/// <summary>
/// A single Intacct API function. Each function becomes one &lt;function&gt; element in the request
/// &lt;content&gt; and produces one &lt;result&gt; in the response, correlated by control id.
/// </summary>
public interface IIntacctFunction
{
    /// <summary>Correlation id echoed back in the matching &lt;result&gt;.</summary>
    string ControlId { get; }

    /// <summary>Builds the inner &lt;function&gt; element (including its controlid attribute).</summary>
    XElement ToXml();
}
