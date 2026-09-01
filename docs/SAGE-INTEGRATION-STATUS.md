# Sage Intacct Integration — Status & Notes

Working notes on the Sage Intacct Web Services integration for UMPA.

_Last updated: 2026-09-01_

## Current status

| Piece | Status | Notes |
|---|---|---|
| Web Services subscription | ✅ Active | Sage confirmed 2026-08-06: Company → Admin → Subscriptions → **Web Services** is Enabled. Separate from the Platform subscription. |
| Sender credentials (Sender ID + password) | ✅ Working | Authenticates the application. |
| Company login (`CompanyId` / `UserId` / `UserPassword`) | ✅ Working | Session establishes for **both** `apiuser` and `webservices_apiuser`. Standardized on `webservices_apiuser`. |
| `getAPISession` | ✅ Working | Returns endpoint `https://api.intacct.com/ia/xml/xmlgw.phtml`. |
| Object permissions (read) | ✅ **Working** | Verified 2026-09-01 across eight objects. See "Resolution" below. |
| Object permissions (write) | ⬜ Untested | Out of scope — the integration is now read-only. See open question #5. |

**Bottom line:** reads work end to end. The `PL04000005` permission wall that blocked this project
for six weeks is gone. **Cause: object permissions were granted to the `webservices_apiuser` role**
between 2026-08-20 and 2026-09-01. It was the role/permissions layer — *not* the user type.

## Resolution (2026-09-01)

Retested after the Sage case had already closed. Every object returns records as
`webservices_apiuser`. No `PL04000005` anywhere.

| Object | Result |
|---|---|
| `ARINVOICE` | ✅ records returned (RECORDNO 726, 727, 780, 783, 1157) |
| `CUSTOMER` | ✅ records returned (RECORDNO 55, 4, 106, 101, 26) |
| `GLACCOUNT` | ✅ records returned (RECORDNO 321, 296, 297, 295, 309) |
| `GLENTRY` | ✅ records returned |
| `GLBATCH` | ✅ records returned |
| `DEPARTMENT` | ✅ records returned |
| `LOCATION` | ✅ records returned |
| `CLASS` | ✅ records returned |

The first three had failed identically on 2026-08-20. The last five had never been probed at all.

**Root cause: permissions were added to the `webservices_apiuser` role.** Confirmed by Kody
2026-09-01. This settles the layer question that dominated the diagnosis — it was **layer 2 (role
object permissions)**, not layer 1 (user type). The Employee-vs-Business user-type hypothesis
recorded below was a reasonable read of Sage's documentation but turned out to be the wrong lead;
it is retained for the record, and to stop it being re-proposed.

> ⚠️ **The specific grants were still not written down.** We know *what layer* was changed but not
> which objects were granted, on which role, or by whom. If `PL04000005` returns, we know where to
> look — but not what "correct" looked like. **Worth capturing now, while it works**: open the role
> assigned to `webservices_apiuser` and record the granted objects and permission levels using the
> checklist below. That converts a working state into a restorable one.

### What the data looks like

Measured 2026-09-01 by probing the API directly, not from the Intacct UI:

| Property | Finding |
|---|---|
| GL date range | **June 2021 → August 2026** (~5.2 years). Nothing before 06/01/2021 — checked back to 2010. |
| `GLENTRY` volume | `RECORDNO` high-water between **150,000 and 200,000** (binary-searched via `RECORDNO >` filters). |
| Entity structure | **Single entity** — `UMPA` (Utah Municipal Power Agency), `ENTITYRECORDNO 1`. `FED` (Federal Hydro), `IMB` (Imbalance), and `MKT` (Market) are *locations* beneath it, not separate entities. |
| Incremental support | `GLBATCH` exposes `WHENCREATED` and `WHENMODIFIED`. Whether `GLENTRY` does is not yet confirmed. |
| Dimension sizes | `DEPARTMENT` has 1 record. All dimensions are small. |

Counts are estimates because the sample app hardcodes `PageSize = 5` and never prints the
`totalcount` attribute the API already returns. Getting exact counts is the first task of the
extraction work.

## Current direction: read-only extraction

**This supersedes the original invoice-posting goal.** The integration is being reversed: rather
than pushing AR invoices *into* Intacct, we are pulling GL data *out* of it into a UMPA-owned SQL
Server datastore, on a scheduled job. Nothing is written back to Sage.

Target objects: `GLENTRY`, `GLBATCH`, `GLACCOUNT`, plus the `DEPARTMENT` / `LOCATION` / `CLASS`
dimensions. `GLDETAIL` still needs to be compared against `GLENTRY` — they are different objects and
picking the wrong one only surfaces when the numbers fail to tie out.

The app will be deployed to the VM that hosts the SQL Server. Note that the current development
machine is **Windows on ARM64**, so a self-contained publish must explicitly target `win-x64` or it
will not run on the destination.

## Reproducing

No rebuild needed — `INTACCT_`-prefixed env vars override `appsettings.json`:

```sh
INTACCT_ReadObject=GLENTRY INTACCT_ReadFields=RECORDNO INTACCT_ReadQuery= \
dotnet run --project samples/UmpaInvoiceService.Sample
```

`INTACCT_ReadQuery` accepts any Intacct filter expression, so date and key filters work directly —
e.g. `INTACCT_ReadQuery="BATCH_DATE > '01/01/2026'"`.

## Configuration in use

Sample config lives in `samples/UmpaInvoiceService.Sample/appsettings.json` (gitignored).
Structure (passwords redacted):

```json
{
  "SenderId": "umpaenergy",
  "SenderPassword": "",
  "CompanyId": "umpaenergy",
  "UserId": "webservices_apiuser",
  "UserPassword": "",
  "EntityId": ""
}
```

> ⚠️ The live `appsettings.json` holds real passwords in plaintext, and the **sender and user
> passwords are the same value** — two different trust domains sharing one secret. It is gitignored,
> but a copy is also written to `samples/UmpaInvoiceService.Sample/bin/Debug/net8.0/appsettings.json`
> on build, so the plaintext exists in two places. Rotate and split these as part of the move to the
> database VM.

---

# History — how this was diagnosed

Everything below is the record of the six-week blocker. Retained because the specific grants were
never written down, and because the false lead it documents is worth not repeating.

## The blocking error

`readByQuery` against any object returned:

```
PL04000005 | You do not have permission for API operation READ_BY_QUERY
on objects of type arinvoice
[Support ID: 9PvcxEBXMLGW216418822-AG%7EaodusP0p7fz-eBbWcXdqqwAAAAI]
```

Earlier occurrence (2026-07-20), same error:

```
[Support ID: eTjKoEBXMLGW133020493-AG~al6NqP0I73h-AiEWyTHNhgAAABk]
```

This was a role/permissions issue in Intacct itself — not a code or credential problem — and it
applied regardless of which API (XML or REST) was used.

## Ruled out: the choice of login user

Sage confirmed on **2026-08-06** that `webservices_apiuser` is the only user registered under
Company → Admin → Web Services Users. Since this project had been authenticating as `apiuser`, the
obvious hypothesis was that we were using a user that isn't authorized for the API at all.

**Tested 2026-08-20 — that hypothesis was wrong.** `webservices_apiuser` authenticated successfully
and then failed with the *identical* `PL04000005` error on every object.

| Login user | `ARINVOICE` | `CUSTOMER` | `GLACCOUNT` |
|---|---|---|---|
| `apiuser` | ❌ PL04000005 | ❌ PL04000005 | ❌ PL04000005 |
| `webservices_apiuser` | ❌ PL04000005 | ❌ PL04000005 | ❌ PL04000005 |

Two conclusions followed: it was not the user (both authenticated, neither had object permissions),
and it was not object-specific (three unrelated objects across AR, order entry, and GL failed the
same way — consistent with the assigned role granting *no* API access at all).

## Retest after Marianne's permission change (2026-08-20, later same day)

Marianne reported changing the permissions. **Retested immediately — no change.** All three objects
still failed identically as `webservices_apiuser`. Support IDs from that run:

```
ARINVOICE  P3JFyEBXMLGW249175554-AG%7Eaod8DP087j3-YzzWUxweBgAAABc
CUSTOMER   K8FKtEBXMLGW249176089-AG%7Eaod8FP0D7rh-ts_WnPYO8QAAAAk
GLACCOUNT  tB2A3EBXMLGW249175554-AG%7Eaod8FP0Y7eC-zQeWXJOGtwAAABg
```

Session establishment succeeded on every run, so this was not a stale-session artifact — each probe
logged in fresh.

## The user-type lead (pursued, ultimately wrong)

From Sage's Help Center article on [Web Services-only users](https://www-p07.intacct.com/ia/docs/en_US/help_action/Administration/Users/web-services-only-users.htm):

> "User type controls the maximum features available to the user, while permissions set what a user
> can actually do within those restrictions."

Notes from 2026-07-20 record that Marianne created both `apiuser` and `webservices_apiuser` as
**Employee**-type accounts. The article names **Business user** as the type with full feature
access. Since all three unrelated objects failed identically — which looks like a blanket cap rather
than a role missing a few objects — user type became the leading suspect.

**This turned out to be wrong.** The blocker cleared when object permissions were added to the
`webservices_apiuser` role — layer 2, not layer 1. The reasoning above was sound given Sage's
documentation, but the inference that identical failures across unrelated modules implied a
user-type cap did not hold: a role granting no API access at all produces the same symptom. Worth
remembering as a diagnostic lesson — *"fails everywhere"* does not distinguish between a cap above
the role and an empty role.

### Also worth keeping

- **Nikhil's navigation path was wrong.** He repeatedly gave it as `Company → Admin → Web Services
  Users`. The documented path is **Company > Admin > Users, roles, and groups > Web Services users**
  — one level deeper. This very likely explains why Marianne could not find the menu on 2026-07-28,
  which in turn led us to wrongly conclude the subscription was missing.
- A Web Services user **cannot log into the web UI at all**, so a UI login cannot be used to isolate
  a credential problem.
- Web Services user passwords **do not expire**, so expiry is never the cause of a login failure.
- A Web Services user does not by itself grant Web Services access — it must be paired with an
  authorized sender ID.

## Verification checklist for `webservices_apiuser`

Retained for use if permissions regress. Requires "Users: List and View" to read these.

**Assigned role** — the layer that actually mattered. Company > Admin > Users, roles, and groups >
Web Services users > `webservices_apiuser` > Edit > assigned role.

Record the granted objects and permission levels **now, while it works** — this is the missing
baseline:

- [ ] `GLENTRY`, `GLBATCH`, `GLACCOUNT` — read
- [ ] `DEPARTMENT`, `LOCATION`, `CLASS` — read
- [ ] `ARINVOICE`, `CUSTOMER` — read
- [ ] Note the **role name** itself, so a regression can be compared against a named object

**User record** — not the cause, but cheap to note alongside:

- [ ] **Status** = Active (not Locked out)
- [ ] **User type** — record the current value for completeness; it was ruled out as the blocker

**Already working — do not change**

- [x] Sender ID authorization (Company > Setup > Web Services authorizations) — accepted.
- [x] Credentials for `webservices_apiuser` — authenticate successfully.

## Open questions

1. ~~**Web Services license**~~ — ✅ Resolved 2026-08-06. Web Services **is** enabled on the company.
   The Platform subscription is a separate product and is not what enables the XML API.
2. ~~**Which user**~~ — ✅ Resolved. `webservices_apiuser` is the only registered Web Services user
   and is now the configured default.
3. ~~**User type**~~ — ✅ **Ruled out.** The fix was at the role level, so user type was never the
   blocker. The Employee-vs-Business theory was wrong; don't re-propose it.
4. **Permissions role** — ✅ **Resolved**, with a caveat. Permissions were added to the
   `webservices_apiuser` role and all eight probed objects now read. **Still open:** nobody recorded
   *which* objects and permission levels were granted, so there is no baseline to restore from if
   this regresses. See the checklist above.
5. **Write permissions** — ⬜ **Out of scope, not resolved.** `CreateArInvoiceFunction` uses the
   legacy `create_invoice` function, permissioned against the **AR module** rather than the
   `ARINVOICE` object, so object-level permissions may not cover it. Untested and irrelevant while
   the integration is read-only — but this is a trap if invoice posting is ever revived.
6. **`GLDETAIL` vs `GLENTRY`** — ❓ **Open.** Different objects serving different purposes. Compare
   `totalcount` on both and confirm which one carries the GL detail finance expects before building
   on either.

## Support case

- **Sage Case #: 00923382** — closed 2026-08-20 without a reply, issue unresolved at the time.
- UMPA had **no support contacts listed** with Sage; added **Kody Carling** and **Garrett**.
- Support engineer: **Nikhil Palli**. External Support Access granted 2026-08-05.

### Case timeline

| Date | Event |
|---|---|
| 2026-07-21 | Nikhil Palli takes ownership. |
| 2026-07-22 | Sage explains Web Services Standard vs. Developer License vs. Platform; points to Company → Admin → Web Services Users for permissions. |
| 2026-07-24 | Case auto-closed for no response. |
| 2026-07-28 | Kody replies — case reopened. Reports that Web Services Users menu is not visible and no Web Services subscription is listed. |
| 2026-08-04 | Sage requests External Support Access. |
| 2026-08-05 | Kody grants External Support Access. |
| 2026-08-06 | Sage confirms Web Services **is** subscribed, and that `webservices_apiuser` is the only Web Services user configured. |
| 2026-08-10 | Case moved to Verify Solution. |
| 2026-08-11 | Kody on vacation until 08-14; case placed On Hold. |
| 2026-08-17 → 08-19 | Three Verify Solution follow-ups. |
| 2026-08-20 | Case closes without a reply. Retest confirms the issue is NOT resolved — same `PL04000005` on all objects for both users. |
| **2026-09-01** | **Retest: all eight probed objects read successfully. Blocker resolved** — after the case had already closed. Cause confirmed by Kody: object permissions were added to the `webservices_apiuser` role. |

## XML vs REST API

This project uses Sage's **XML Web Services API** (session-based, sender ID + login).

- Sage also has a **REST API**, GA since the **2025 R1** release (Feb 2025), using OAuth 2.0.
  Portal: <https://developer.sage.com/intacct>.
- The XML API is **not deprecated** — both run in parallel, so this project isn't on borrowed time.
- REST would **not** have avoided the permissions blocker — it was at the role/object level and
  applies to both APIs.
- The classic XML reference lives at <https://developer.intacct.com/>.

## Reference links from Sage

- Web Services subscription overview — <https://community.intacct.com/support/s/article/7159?language=en_US>
- Web Services users — <https://community.intacct.com/support/s/article/8370?language=en_US>
- Web Services-only users (Help Center) — <https://www-p07.intacct.com/ia/docs/en_US/help_action/Administration/Users/web-services-only-users.htm>
- Permissions TOC — <https://www.intacct.com/ia/docs/en_US/help_action/Administration/Permissions/aa-TOC-permissions.htm>

## Key people

- **Ari** — Sage contact; suggested opening the support ticket.
- **Marianne** — set up the `apiuser` and `webservices_apiuser` accounts at UMPA.
- **Garrett** — added as a Sage support contact alongside Kody.
- **Nikhil Palli** — Sage Intacct Support engineer on case 00923382.
