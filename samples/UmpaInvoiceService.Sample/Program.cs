using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using UmpaInvoiceService.Intacct;
using UmpaInvoiceService.Intacct.Functions;
using UmpaInvoiceService.Intacct.Services;

// Load config from appsettings.json (gitignored) and/or INTACCT_-prefixed environment variables.
// Env vars win, so you can keep secrets out of files entirely:
//   INTACCT_SenderPassword=... INTACCT_UserPassword=... dotnet run
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("INTACCT_")
    .Build();

var options = new IntacctClientOptions
{
    SenderId = Required(config, "SenderId"),
    SenderPassword = Required(config, "SenderPassword"),
    CompanyId = config["CompanyId"] ?? "umpaenergy",
    UserId = config["UserId"] ?? "webservices_apiuser",
    UserPassword = Required(config, "UserPassword"),
    EntityId = config["EntityId"],
};

using var client = new IntacctClient(options);

try
{
    // 1) Establish a session (optional to call explicitly — ExecuteAsync does it automatically).
    var session = await client.EnsureSessionAsync();
    Console.WriteLine($"Session established. Endpoint: {session.Endpoint}");

    // 2) Example read — pull a few AR invoices to confirm connectivity.
    // Object/query/fields can be overridden via INTACCT_ReadObject / INTACCT_ReadQuery / INTACCT_ReadFields
    // for quick ad-hoc testing without recompiling.
    var read = new ReadByQueryFunction(config["ReadObject"] ?? "ARINVOICE")
    {
        Fields = config["ReadFields"] ?? "RECORDNO,RECORDID,CUSTOMERID,TOTALDUE,STATE",
        Query = config["ReadQuery"] ?? "STATE = 'Posted'",
        PageSize = 5,
    };
    var response = await client.ExecuteAsync(read);
    var result = response[read.ControlId];
    if (!result.Success)
    {
        Console.Error.WriteLine($"Read of '{read.ObjectName}' failed: {string.Join("; ", result.Errors)}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Diagnostics.Explain(string.Join("; ", result.Errors), options));
        Environment.ExitCode = 1;
    }
    else
    {
        var records = result.Data?.Elements().ToList() ?? new List<XElement>();
        Console.WriteLine($"Read of '{read.ObjectName}' succeeded — {records.Count} record(s) returned:");
        foreach (var record in records)
            Console.WriteLine(record);
    }

    // 3) Example write — post an AR invoice. Uncomment and use real ids once you're ready.
    // var invoices = new IntacctInvoiceService(client);
    // var key = await invoices.CreateInvoiceAsync(new CreateArInvoiceFunction
    // {
    //     CustomerId = "CUST-0001",
    //     InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
    //     Description = "Umpa Energy — monthly service",
    //     Lines =
    //     [
    //         new ArInvoiceLine { GlAccountNo = "4000", Amount = 250.00m, Memo = "Consulting" },
    //     ],
    // });
    // Console.WriteLine($"Created AR invoice key: {key}");
}
catch (IntacctException ex)
{
    Console.Error.WriteLine($"Intacct error: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(Diagnostics.Explain(ex, options));
    Environment.ExitCode = 1;
}

static string Required(IConfiguration c, string key) =>
    c[key] is { Length: > 0 } v
        ? v
        : throw new InvalidOperationException(
            $"Missing config '{key}'. Set it in appsettings.json or as env var INTACCT_{key}.");
