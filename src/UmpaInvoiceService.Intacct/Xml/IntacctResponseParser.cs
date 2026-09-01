using System.Xml;
using System.Xml.Linq;

namespace UmpaInvoiceService.Intacct.Xml;

/// <summary>
/// Parses the Intacct XML response, surfacing control-level and authentication-level failures as
/// exceptions and returning per-function results for everything else.
/// </summary>
internal static class IntacctResponseParser
{
    public static IntacctResponse Parse(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new IntacctException($"Intacct response was not valid XML: {ex.Message}");
        }

        var response = doc.Element("response")
            ?? throw new IntacctException("Intacct response is missing its <response> root element.");

        // Control-level failure (bad sender credentials, malformed envelope, etc.). The
        // <errormessage> sits under <response> as a sibling of <control>, so scope to <response>.
        var control = response.Element("control");
        if (StatusOf(control) == "failure")
            throw new IntacctException("Intacct rejected the request (control error).", ParseErrors(response));

        var operation = response.Element("operation")
            ?? throw new IntacctException("Intacct response is missing its <operation> element.", ParseErrors(response));

        // Authentication-level failure (bad login credentials, unauthorized sender, expired session).
        var auth = operation.Element("authentication");
        if (StatusOf(auth) == "failure")
            throw new IntacctException("Intacct authentication failed.", ParseErrors(operation));

        var results = new List<IntacctResult>();
        foreach (var result in operation.Elements("result"))
        {
            var success = StatusOf(result) == "success";
            var data = result.Element("data");
            results.Add(new IntacctResult
            {
                ControlId = (string?)result.Element("controlid") ?? string.Empty,
                Function = (string?)result.Element("function") ?? string.Empty,
                Success = success,
                Errors = success ? Array.Empty<IntacctError>() : ParseErrors(result),
                Data = data,
                TotalCount = IntAttribute(data, "totalcount"),
                NumRemaining = IntAttribute(data, "numremaining"),
                ResultId = (string?)data?.Attribute("resultId"),
                Key = (string?)result.Element("key"),
            });
        }

        return new IntacctResponse { Results = results };
    }

    // <data> carries paging state as attributes: totalcount, numremaining, resultId.
    private static int IntAttribute(XElement? el, string name) =>
        int.TryParse((string?)el?.Attribute(name), out var value) ? value : 0;

    private static string? StatusOf(XElement? el) => (string?)el?.Element("status");

    private static IReadOnlyList<IntacctError> ParseErrors(XElement? scope)
    {
        if (scope is null) return Array.Empty<IntacctError>();
        return scope.Descendants("error")
            .Select(e => new IntacctError(
                (string?)e.Element("errorno"),
                (string?)e.Element("description"),
                (string?)e.Element("description2"),
                (string?)e.Element("correction")))
            .ToList();
    }
}
