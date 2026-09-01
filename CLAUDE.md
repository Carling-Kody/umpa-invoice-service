# umpa-invoice-service — project instructions

Read this first. It orients a fresh session; the detail lives in `docs/`.

## What this is

A .NET 8 client for the **Sage Intacct XML Web Services API**, for UMPA (Utah Municipal Power
Agency, Intacct company `umpaenergy`).

**Current goal: read-only extraction.** Pull GL data out of Intacct into a UMPA-owned SQL Server
database on a nightly scheduled job. **Nothing is written back to Sage.** The repo name and the
original invoice-posting code predate this direction — don't take the name as the current intent.

## Read these before planning work

| Document | What it holds |
|---|---|
| `docs/EXTRACTION-PLAN.md` | The approved implementation plan — stages, schema, dependency decisions, verification. Start here for "what's next". |
| `docs/API-DATA-INVENTORY.md` | What the Intacct API actually exposes: objects, field lists, volumes, which dimensions carry data. All measured against the live API, not from docs. |
| `docs/SAGE-INTEGRATION-STATUS.md` | Connection/permissions state and the history of a six-week blocker. Read the History section before touching anything permissions-related. |

## Current state (as of 2026-09-01)

- **API access works.** All eight probed objects read successfully. This was blocked from July to
  September by an Intacct permissions problem; it was fixed by adding object permissions to the role
  assigned to `webservices_apiuser`.
- **The client library is done for extraction purposes** — sessions, paged reads, structured errors.
- **Nothing has been built against SQL Server yet.** That's the next body of work.
- **The write path (`CreateArInvoiceFunction`) has never been verified.** It uses the legacy
  `create_invoice` function, permissioned against the AR module rather than the `ARINVOICE` object,
  so working reads do not imply working writes. Out of scope — leave it alone.

## Conventions

- `src/UmpaInvoiceService.Intacct` has **zero NuGet dependencies** and builds with
  `TreatWarningsAsErrors`. **Keep it that way** — anything needing external packages goes in a
  project that consumes it, not in the library.
- Async methods take `CancellationToken ct = default` last and use `.ConfigureAwait(false)` in
  library code.
- `public sealed class` with `required` / `init` properties; `record` for value types; file-scoped
  namespaces; XML doc comments on public members explaining *why*, not just what.
- Operator-facing troubleshooting belongs in the console app (see
  `samples/UmpaInvoiceService.Sample/Diagnostics.cs`), not in the library.
- `Functions/` holds one class per Intacct API function.

## Gotchas that have already cost time

- **`IntacctReadService.ReadAllAsync` is the extraction primitive.** It already handles
  `readByQuery` → `readMore` paging and streams results. Don't rewrite paging.
- **A paged read cannot be resumed on a new session.** The server-side result set belongs to the
  session that opened it. If a session drops mid-walk, re-run the whole query — which is why the
  plan chunks reads by month.
- **`IntacctResult.TotalCount` reports `0` when the attribute is absent**, which is
  indistinguishable from a genuinely empty result. For anything that matters, read the attribute off
  the raw `Data` XElement.
- **A permission failure looks identical across all objects whether the cause is the user type or an
  empty role.** We chased the wrong layer for weeks. Start at the role assigned to the web services
  user.

## Credentials

Config lives in `samples/UmpaInvoiceService.Sample/appsettings.json` (gitignored), overridable by
`INTACCT_`-prefixed environment variables, which win. Example:

```sh
INTACCT_ReadObject=GLENTRY INTACCT_ReadFields=RECORDNO INTACCT_ReadQuery= \
dotnet run --project samples/UmpaInvoiceService.Sample
```

⚠️ The live `appsettings.json` holds real passwords in plaintext, the sender and user passwords are
currently **the same value**, and the build copies the file into `bin/`. Never commit it, never
paste its contents, and prefer environment variables. Rotating and splitting these is an open task.

## Environment notes

- Requires the **.NET 8 SDK**.
- This project was developed on a Windows **ARM64** VM and is being moved to the Windows server that
  hosts SQL Server. If publishing from an ARM64 machine for an x64 target, publish
  framework-dependent or pass `-r win-x64` explicitly — a self-contained ARM64 build will not start
  on the server.
- Git remote uses **HTTPS**, not SSH.
