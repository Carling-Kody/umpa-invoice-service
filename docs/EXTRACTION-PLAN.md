# Plan: correct the docs, then build a read-only GL extraction pipeline

> **Progress — updated 2026-09-01**
>
> - ✅ **Stage 0 (documentation) is complete.** `SAGE-INTEGRATION-STATUS.md`, `README.md`, and
>   `Diagnostics.cs` have all been corrected; build is clean.
> - ➡️ **Stage 1 (probe tooling) is next.** Note that some Stage 1 questions were already answered
>   ad hoc — see `API-DATA-INVENTORY.md` for the field lists, the `GLENTRY` vs `GLDETAIL`
>   comparison, and sampled dimension usage. What remains for Stage 1 is exact row counts, a
>   full-population count for dimensions, and probing the `VENDOR` / `ITEM` masters.
> - One plan assumption has since been settled: the permissions blocker's root cause **was** the
>   role's object permissions, not the user type.

## Context

Two things changed today (2026-09-01).

**1. The permissions blocker is gone.** For six weeks this project was dead on Sage Intacct error
`PL04000005` — "You do not have permission for API operation READ_BY_QUERY". Authentication always
worked; every data call failed. Sage case 00923382 closed 2026-08-20 without a fix. Retesting today
succeeded on every object. Someone landed the change between 08-20 and today; **we don't know which
layer was fixed** (user type vs. assigned role), so it could regress the same undocumented way.
`docs/SAGE-INTEGRATION-STATUS.md` still says "blocked" and is now wrong.

**2. The integration direction flipped.** The repo was built to *push* AR invoices into Intacct.
The need is the reverse: **pull GL data out of Intacct into UMPA's SQL Server**, read-only. No
writes back to Sage. The app will be deployed to the VM that hosts the database.

## Measured facts (probed today, not assumed)

| Finding | Detail |
|---|---|
| Readable | `ARINVOICE`, `CUSTOMER`, `GLACCOUNT`, `GLENTRY`, `GLBATCH`, `DEPARTMENT`, `LOCATION`, `CLASS` — all return records |
| Date range | **June 2021 → August 2026** (~5.2 years). Nothing before 06/01/2021; checked back to 2010 |
| GLENTRY volume | `RECORDNO` high-water between **150k and 200k** |
| Entity structure | **Single entity** — `UMPA`, `ENTITYRECORDNO 1`. `FED`/`IMB`/`MKT` are locations beneath it |
| Incremental viability | `GLBATCH` exposes `WHENCREATED`/`WHENMODIFIED`; `ReadByQueryFunction.Query` is a plain string |
| Dimension sizes | `DEPARTMENT` = 1 record. All dimensions trivial |

**The volume number drives the whole design.** ~175k rows at `PageSize=1000` is ~200 API calls —
single-digit minutes. So: **full rebuild every run.** That deletes the watermark table, the
company-timezone filter bugs, the missed-hard-deletes problem, and all resume state. Revisit only
if GLENTRY passes ~1M rows.

---

## Stage 0 — Correct the documentation

Pure prose, no code risk. Stops the next session re-litigating a dead blocker.

**`docs/SAGE-INTEGRATION-STATUS.md`** — restructure, don't patch:
- Status table: object permissions ❌ **Still blocked** → ✅ Working (verified 2026-09-01). Update
  `_Last updated:_`.
- Rewrite "Bottom line" (currently "the only blocker is still permissions") → reads confirmed on all
  eight probed objects; resolved between 08-20 and 09-01 by a change we did not make and **cannot
  attribute**.
- Add a resolution section with today's evidence: the newly-probed GL objects, the June 2021 →
  Aug 2026 range, the 150–200k estimate, single-entity finding.
- **Preserve the diagnostic history, demote it** under "History — how this was diagnosed". The test
  matrices, Support IDs, and user-type-vs-role analysis stay valuable if permissions regress.
- Open questions: #3 (user type) and #4 (role) → "no longer blocking, root cause never confirmed" —
  do *not* mark cleanly solved. #5 (write permissions) → **out of scope, not resolved**.
- Timeline: add `2026-09-01 | All objects read successfully. Blocker resolved; case had already
  closed 08-20 without a reply.`
- Add "Current direction": read-only GL extraction to SQL Server, superseding invoice posting.

**`README.md`** — three factual errors plus drift:
- Opening line is backwards: "Backend service for **pushing** invoice data into Sage Intacct."
- Claims ".NET 8 SDK — **not currently installed on this machine**." It is: `8.0.422`.
- Credential checklist says `UserId = apiuser`; `appsettings.json` already uses `webservices_apiuser`.
- Layout tree omits `ReadMoreFunction.cs`, `Services/IntacctReadService.cs`, `IntacctException.cs`,
  `IntacctSession.cs`. Add a "Reading data" example using `IntacctReadService.ReadAllAsync`.

**`samples/UmpaInvoiceService.Sample/Diagnostics.cs`** — light touch. The `PL04000005` branch
(`:52-67`) is still correct and should **stay** — it's exactly the guidance needed if permissions
regress. Only soften "If ALL objects fail identically, suspect layer 1" to note this pattern
occurred and resolved on 2026-09-01 without confirming the cause.

---

## Stage 1 — Probe (hours, no SQL, no new dependencies)

Today's probe used the sample, which hardcodes `PageSize = 5` and never prints `TotalCount` — which
is why volume had to be binary-searched. Fix that first; it makes every later stage measurable.

New console **`src/UmpaInvoiceService.Extract.Cli/`** with a `probe` verb. Reuses
`IntacctReadService`/`ReadByQueryFunction`; no library changes except one additive function below.

Must answer:
1. **`GLDETAIL` vs `GLENTRY`** — different objects (GL detail reporting vs. journal-entry lines).
   Compare `TotalCount` on both and check whether a known AR invoice posting appears in GLENTRY.
   **Picking the wrong one surfaces only when finance can't tie out.** Do not assume.
2. **Exact `TotalCount` per object**, replacing today's estimates.
3. **Does `GLENTRY` expose `WHENMODIFIED`?** Not needed for full-rebuild, but determines whether
   incremental is even possible later.
4. **Which dimension columns are actually populated on GLENTRY** — sample 100 rows, count non-null
   across `DEPARTMENTID`/`LOCATIONID`/`CLASSID`/`PROJECTID`/`CUSTOMERID`/`VENDORID`. Derive the
   dimension list from that rather than assuming DEPARTMENT/LOCATION/CLASS is complete.
5. **Field lists per object** via a new `Functions/LookupFunction.cs` (additive, pure BCL, keeps the
   library zero-dependency). Note `lookup` and `READ_BY_QUERY` are **separately permissioned** — a
   successful lookup does not prove you can read rows.

Read `totalcount` **off the raw `XElement`** (`result.Data?.Attribute("totalcount")`), not off
`IntacctResult.TotalCount` — the parser turns a missing attribute into `0`
(`Xml/IntacctResponseParser.cs:64-65`), so "attribute absent" and "genuinely empty" are
indistinguishable through the typed property. They mean very different things.

Dump raw `lookup` response XML to disk on first run and write the parser against real bytes.

---

## Stage 2 — SQL foundation

⚠ **No local SQL is possible on this box.** This VM is Windows on **ARM64**; SQL Server LocalDB has
no ARM64 build and Developer Edition is x64-only. Since the app is moving to the DB VM anyway,
develop against that server directly (or work on the DB VM). Prove `Microsoft.Data.SqlClient`
connects from ARM64 as the very first task — before writing the sink.

New library **`src/UmpaInvoiceService.Extract/`** (separate from `Extract.Cli` so the mapping and
load logic are testable; the existing `Intacct` library stays pure-BCL and untouched).

**Dependencies — deliberately minimal**, matching the repo's near-zero-dependency character:
- `Microsoft.Data.SqlClient` — gives `SqlCommand` *and* `SqlBulkCopy`, which is all we need.
  Be honest that it pulls ~10 transitive packages; there is no lighter supported option.
- `Microsoft.Extensions.Logging.Abstractions` in `Extract`, `.Console` in `Cli`.
- **No EF Core** (change tracking and migrations buy nothing for a one-way bulk load of
  runtime-discovered schema). **No Dapper** — it'd save ~60 lines against ~8 total commands; adding
  it later is a one-line csproj change, so take the minimal option now.
- Test project `tests/UmpaInvoiceService.Extract.Tests/` (xunit) — establishes the convention.

Add a root `Directory.Build.props` for `TargetFramework`/`Nullable`/`TreatWarningsAsErrors` —
currently duplicated across csprojs, and the sample is *missing* `TreatWarningsAsErrors`.

**Schema** — `.sql` files as embedded resources, run idempotently by an `install-schema` verb:
- `stage.*` (heaps, `SqlBulkCopy` target) and `intacct.*` (final, keyed).
- One table per object, `RecordNo` as PK, **typed columns for the fields we query** (dates, amounts,
  ids, account, dimensions, `STATE`) **plus `RawJson nvarchar(max)`** holding the full record.
- **The raw column is the highest-value decision here.** The field list is *discovered*, not known;
  the expensive, permission-fragile part is pulling from Intacct, and shredding is free. With the
  raw payload, a wrong column type or newly-needed field is an `UPDATE … FROM OPENJSON(RawJson)`
  against data you already hold — not another backfill against an API that spent months refusing to
  talk to us.
- `decimal(19,4)` for amounts, not `(19,2)` — multi-currency and exchange-derived fields aren't 2dp.
- `WITH (DATA_COMPRESSION = PAGE)` from the start; retrofitting is an offline rebuild.
- **No foreign keys between `intacct.*` tables** — a FK would make load order significant and
  hard-fail the whole load if one object is permission-blocked while another isn't, which given this
  company's history is a realistic Tuesday.
- `meta.ExtractRun` / `meta.ExtractRunObject` audit tables: object, started/finished UTC, row count,
  status, error number and text. This is what you query when the scheduled job silently stops.

**Load pattern:** bulk into `stage`, verify row count, swap into `intacct` inside a transaction.
Restart-safe by construction — a failed run leaves the live table untouched, and re-running is the
entire recovery procedure.

*Trade-off accepted:* full-rebuild-and-swap discards per-row `FirstSeenUtc` and soft-delete history.
Intacct is the system of record and dimensions are marked inactive rather than deleted, so this
costs little. If change history is later wanted, the upgrade is staging + hash-guarded upsert +
soft-delete sweep — but don't build it speculatively.

---

## Stage 3 — Correctness and resilience

The codebase currently has **zero** retry, backoff, or session-expiry handling. A long walk will
eventually hit a session timeout.

**Integrity check — the most important guard in the plan.** Capture `totalcount` from the first page
and assert `rowsFetched == totalCount` before marking an object complete; mismatch throws and
retries. For a financial extract **silent truncation is the worst failure mode** — a table that
looks fine and doesn't tie out, with nothing erroring. This is ~5 lines in `Extract` and it closes
the `NumRemaining`-vs-absent parser gap without touching the library.

**Chunking instead of resumable pagination.** A mid-walk re-login invalidates the server-side result
set, so a `readMore` walk *cannot* be resumed. Don't try. Instead partition each object into
deterministic chunks (one per fiscal month for GL: `ENTRY_DATE BETWEEN …`), each a complete
standalone query. Every failure — transport, 429, 5xx, session expiry, process kill — is handled
identically: discard the client, create a fresh one, re-run the whole chunk. Safe because the query
is deterministic and we rebuild into staging anyway. Month chunks are also operator-legible:
"re-pull August 2026" is a request finance can make and you can run.

**Client recycling is cheap** — verified in `IntacctClient.cs:120-124`, `Dispose()` only disposes the
`HttpClient` when it created it (`_ownsHttp`). So passing in a long-lived `HttpClient` lets you
dispose and recreate `IntacctClient` freely for a fresh session, with no socket churn and **no
library change**. Recycle proactively every N chunks, so expiry is a non-event rather than a
primary code path.

**Retry** via a `DelegatingHandler` passed to `IntacctClient(options, httpClient)` — the only
injection point. Exponential backoff with jitter, honor `Retry-After`, and set an explicit
`HttpClient.Timeout` (~180s; the 100s default is tight for a 1000-row page).

**Error classifier: table-driven from config, not hardcoded.** Seed `PL04000005 → PermissionDenied`;
default everything unrecognized to *transient* (safe, since chunk replay is idempotent) and **log the
full error text**. We do not reliably know Intacct's session-expiry or rate-limit error numbers —
promote them into the table once real runs reveal them rather than guessing now.

**Serial execution.** Default degree of parallelism 1. The API is the bottleneck and the fragile
part; SQL is not. Revisit only with measurements.

**Per-object config with an `Enabled` flag.** Given permissions here have been granted piecemeal and
can regress, an operator must be able to switch an object off — or on — without a rebuild. Objects
skipped for permission are recorded as `Skipped_NoPermission` and the run still succeeds for the rest.

---

## Stage 4 — Deployment to the database VM

1. **⚠ Architecture mismatch.** Dev box is **ARM64**; the DB VM is almost certainly x64. A
   self-contained publish built here emits ARM64 binaries that will not run there. Publish
   framework-dependent (`dotnet publish -c Release`, needs .NET 8 runtime on target) or explicitly
   `-r win-x64 --self-contained`. **Confirm the target's architecture before first deploy.**
2. **Verify outbound HTTPS to `api.intacct.com` from the DB VM.** This VM's Sophos policy already
   blocks SSH outright; the DB VM may have its own egress rules. Check before assuming it'll run.
3. **Auth gets simpler on-box.** Use `Integrated Security=true` under a dedicated service account —
   **no database password to store anywhere.** The connection string becomes non-secret config.

**Secrets.** That leaves only the Intacct credentials. The status doc already flags that sender and
user passwords are **identical** — different trust domains sharing one secret; rotate and split as
part of this move. Keep the existing `INTACCT_`-prefixed env-var override
(`samples/.../Program.cs:13`) and set them scoped to the service account. **Do not replicate
`UmpaInvoiceService.Sample.csproj:23`**, which copies `appsettings.json` into `bin\` — that's why
plaintext passwords currently exist in two places.

**Task Scheduler**, daily, as that service account, "run whether logged on or not". Set "Start in"
(classic footgun; use `AppContext.BaseDirectory` for config paths regardless). **Exit codes are the
alerting hook**: `0` all objects succeeded, `1` partial (some skipped for permission — a run that
pulled 4 of 5 objects did useful work and shouldn't look like a crash), `2` fatal.

---

## Verification

| Stage | Proof | Pass criterion |
|---|---|---|
| 0 | `dotnet build`; re-run the object probe | Docs match observed reality |
| 1 | `probe` verb over all objects + GLDETAIL | Definitive `TotalCount` per object; GLENTRY-vs-GLDETAIL decided on evidence |
| 2 | Run `install-schema` twice; connect from ARM64 | Second run: no errors, no changes |
| 2 | Load `DEPARTMENT` (1 row), then `LOCATION`/`CLASS`, then `GLACCOUNT` | `COUNT(*)` == Intacct's `TotalCount` exactly |
| 3 | Fake `HttpMessageHandler` emitting 429/503/timeout; truncated-response fixture | Backoff as expected; truncated chunk **throws** rather than short-loading |
| 3 | Hard-kill mid-run (`taskkill`), re-run | Live tables unchanged by the failed run; re-run completes clean |
| 4 | **Debits equal credits per batch:** `SELECT BatchNo FROM intacct.GlEntry GROUP BY BatchNo HAVING SUM(Amount * TrType) <> 0` | **Zero rows.** Validates `Amount` *and* `TrType` shredding end to end |
| 4 | **Tie-out:** `SUM(Amount)` by account by period vs. Intacct's own Trial Balance | Matches to the cent |
| 4 | Flip an object to a known-blocked one | Others load, exit code 1, `Skipped_NoPermission` logged |

The last two are the ones that matter. Row counts prove data moved; the **tie-out proves it moved
correctly**, and it's the only evidence that will satisfy whoever consumes this. The pipeline is not
done until it passes.

---

## Open items

- **Server/database name** for the on-prem SQL Server, and confirmation the service account gets
  write access.
- **Target VM architecture** (see ARM64 warning).
- **Who consumes this, with what tool?** Power BI over a star schema argues for more typed columns
  and derived dimension tables; ad-hoc SQL makes the raw-JSON-plus-core-columns shape ideal as
  designed.
- Whether an existing warehouse schema/naming convention should be conformed to rather than
  inventing `intacct.*`.
- **Nothing in this repo is committed** — `src/`, `docs/`, `samples/`, `.gitignore`, and the `.sln`
  are all untracked against a single `Initial commit`. Commit before new work lands, and stage
  `.gitignore` **first** so `bin/`/`obj/` and the credential-bearing `appsettings.json` never enter
  history.
