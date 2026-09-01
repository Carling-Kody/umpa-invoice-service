# umpa-invoice-service

.NET client for the Sage Intacct **XML Web Services** API (session-based authentication), plus the
tooling built on top of it.

The current focus is **read-only extraction**: pulling GL data out of Intacct into a UMPA-owned SQL
Server datastore on a scheduled job. The library also supports posting AR invoices, which is where
the project started — see [Writing data](#writing-data) and the caveat there.

See [`docs/SAGE-INTEGRATION-STATUS.md`](docs/SAGE-INTEGRATION-STATUS.md) for connection status,
permissions history, and what the data actually looks like.

## Layout

```
UmpaInvoiceService.sln
├── src/UmpaInvoiceService.Intacct/      # reusable Intacct client library (zero NuGet deps)
│   ├── IntacctClient.cs                 # session mgmt + request/response pipeline
│   ├── IntacctClientOptions.cs          # sender + login credentials
│   ├── IntacctSession.cs                # session id + per-session endpoint
│   ├── IntacctException.cs              # structured Intacct errors
│   ├── Functions/                       # one class per Intacct API function
│   │   ├── ApiSessionFunction.cs        #   getAPISession
│   │   ├── ReadByQueryFunction.cs       #   readByQuery (generic read, page 1)
│   │   ├── ReadMoreFunction.cs          #   readMore (subsequent pages)
│   │   └── CreateArInvoiceFunction.cs   #   create_invoice (AR invoice)
│   ├── Services/
│   │   ├── IntacctReadService.cs        # paged reads — the basis of data extraction
│   │   └── IntacctInvoiceService.cs     # high-level invoice-posting layer
│   └── Xml/                             # envelope builder + response parser
└── samples/UmpaInvoiceService.Sample/   # runnable console demo + operator diagnostics
```

## Prerequisites

- **.NET 8 SDK**
- **Sage Intacct credentials** (see checklist below)

## Credential checklist

The XML API needs **two** credential layers:

| Credential | What it is | Where it comes from |
|---|---|---|
| `SenderId` / `SenderPassword` | Identifies *your application* | Sage Web Services **developer license** |
| `CompanyId` / `UserId` / `UserPassword` | Authenticates into *your Intacct company* | A **Web Services user** in the company |
| Sender authorization | Company must trust your `SenderId` | Company → Setup → **Web Services authorizations** |

For UMPA: `CompanyId = umpaenergy`, `UserId = webservices_apiuser` — the only user registered under
Company → Admin → Users, roles, and groups → Web Services users.

## Configuration

Copy the example and fill in your values (the real file is gitignored):

```sh
cp samples/UmpaInvoiceService.Sample/appsettings.example.json \
   samples/UmpaInvoiceService.Sample/appsettings.json
```

Or keep secrets out of files entirely by using `INTACCT_`-prefixed environment variables:

```sh
INTACCT_SenderId=...           \
INTACCT_SenderPassword=...      \
INTACCT_UserPassword=...        \
dotnet run --project samples/UmpaInvoiceService.Sample
```

Environment variables override `appsettings.json`.

## Build & run

```sh
dotnet build
dotnet run --project samples/UmpaInvoiceService.Sample
```

The sample establishes a session and reads a few records to confirm connectivity. The object,
fields, and filter can be overridden without recompiling:

```sh
INTACCT_ReadObject=GLENTRY INTACCT_ReadFields=RECORDNO INTACCT_ReadQuery= \
dotnet run --project samples/UmpaInvoiceService.Sample
```

When a call fails, `Diagnostics.Explain` translates the Intacct error number into plain-English
next steps — permission problems in particular are easy to misread as credential problems.

## Reading data

`IntacctReadService` handles pagination. `readByQuery` returns one page (max 1000 records) and parks
the rest in a server-side result set; `readMore` walks it. `ReadAllAsync` does that transparently and
**streams**, so it works on objects far larger than memory:

```csharp
using var client = new IntacctClient(options);
var reader = new IntacctReadService(client);

var query = new ReadByQueryFunction("GLENTRY")
{
    Fields   = "RECORDNO,BATCHNO,ACCOUNTNO,AMOUNT,TR_TYPE,ENTRY_DATE",
    Query    = "ENTRY_DATE BETWEEN '01/01/2026' AND '01/31/2026'",
    PageSize = 1000,
};

await foreach (var record in reader.ReadAllAsync(query))
{
    // record is the raw <glentry> XElement
}
```

Two constraints worth knowing:

- The server-side result set belongs to the session that opened it, so a single enumeration must run
  against **one** `IntacctClient`. It cannot be resumed on a new session — if a session drops
  mid-walk, re-run the whole query. Keeping queries narrow (a month at a time) makes that cheap.
- `ReadAllToListAsync` buffers everything into a `List<XElement>`. Fine for dimensions, wrong for
  fact tables.

## Writing data

```csharp
using var client = new IntacctClient(options);              // reuse per company connection
var invoices = new IntacctInvoiceService(client);

var key = await invoices.CreateInvoiceAsync(new CreateArInvoiceFunction
{
    CustomerId  = "CUST-0001",
    InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
    Lines = [ new ArInvoiceLine { GlAccountNo = "4000", Amount = 250m, Memo = "Consulting" } ],
});
```

> ⚠️ **The write path is unverified.** `CreateArInvoiceFunction` uses the legacy `create_invoice`
> function, which is permissioned against the **AR module** rather than the `ARINVOICE` object — so
> working reads do not imply working writes. Nothing has been posted to Intacct from this codebase.

The client runs `getAPISession` automatically on first use and reuses the session id thereafter.

## Notes

- Never commit real credentials. `appsettings.json` and `.env` files are gitignored;
  `appsettings.example.json` (placeholders only) is tracked. Note the csproj copies
  `appsettings.json` into `bin/`, so plaintext secrets exist in two places on a built tree.
- `src/UmpaInvoiceService.Intacct` has **zero NuGet dependencies** and builds with
  `TreatWarningsAsErrors`. Keep it that way — put anything needing external packages in a project
  that consumes it.
- `CreateArInvoiceFunction` covers a common field set — extend it as your invoice data needs
  more fields (terms, currency, dimensions, custom fields).
