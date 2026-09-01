# Intacct API — Data Inventory

What the Sage Intacct API actually exposes for UMPA, **measured against the live API on
2026-09-01**, not taken from documentation.

Company: `umpaenergy` · User: `webservices_apiuser` · Single entity (`UMPA`, `ENTITYRECORDNO 1`).

## Coverage

| Property | Measured |
|---|---|
| GL history | **June 2021 → August 2026** (~5.2 years). Nothing exists before 06/01/2021 — probed back to 2010. |
| `GLENTRY` volume | `RECORDNO` high-water between **150,000 and 200,000**. Binary-searched with `RECORDNO >` filters; exact counts still to be confirmed. |
| Entity structure | **Single entity.** `FED` (Federal Hydro), `IMB` (Imbalance), `MKT` (Market), `WVP` (West Valley) and others are *locations* beneath `UMPA`, not separate books. |

At this volume a complete reload is roughly 200 API calls at `PageSize=1000` — single-digit minutes.
That is why the plan rebuilds fully each run rather than syncing incrementally.

## Readable objects

All confirmed returning records as `webservices_apiuser`:

| Object | Contents |
|---|---|
| `GLENTRY` | Journal entry lines — the ledger itself. 70 fields. |
| `GLDETAIL` | GL detail reporting view. 81 fields. |
| `GLBATCH` | Journal batch headers. |
| `GLACCOUNT` | Chart of accounts. |
| `LOCATION` | Location master, with parent/entity hierarchy. |
| `DEPARTMENT` | Department master — **one record company-wide**. |
| `CLASS` | Class dimension master. |
| `CUSTOMER` | Customer master. |
| `ARINVOICE` | AR invoice headers. |

**Not yet probed:** `VENDOR` and `ITEM` masters. Both are needed — vendor and item IDs are populated
on GL entries (see below), so without these masters those columns stay as bare IDs.

## `GLENTRY` vs `GLDETAIL`

Both read successfully. They are not redundant.

| | `GLENTRY` | `GLDETAIL` |
|---|---|---|
| Amounts | Signed `AMOUNT` + `TR_TYPE` flag | Separate `DEBITAMOUNT` / `CREDITAMOUNT`, plus `TRX_DEBITAMOUNT` / `TRX_CREDITAMOUNT` |
| Source document | `DOCUMENT` only | `DOCNUMBER`, `REFERENCENO`, `RECORDID`, `TOTALDUE`, `TOTALPAID`, `WHENDUE`, `WHENPAID` |
| Batch link | `BATCHNO` | `BATCHKEY`, `BATCH_NO`, `BATCH_STATE`, `GLENTRYKEY` |
| Fields | 70 | 81 |
| Best for | A faithful ledger copy; debits/credits net to zero per batch, so it self-validates | Reporting directly from SQL; pre-split amounts and a link back to the originating document |

**Recommendation: extract both.** Same source, small volume, and it removes the risk of choosing
wrong and discovering it when a report fails to tie out.

## `GLENTRY` field list (70)

```
ACCOUNTKEY ACCOUNTNO ACCOUNTTITLE ADJ ALLOCATION ALLOCATIONKEY AMOUNT BASECURR
BASELOCATION BASELOCATION_NAME BASELOCATION_NO BATCHNO BATCHTITLE BATCH_DATE
BATCH_NUMBER BILLABLE BILLED CLASSDIMKEY CLASSID CLASSNAME CLEARED CLRDATE
CREATEDBY CREATEDBYLOGINID CURRENCY CUSTOMERDIMKEY CUSTOMERID CUSTOMERNAME
DEPARTMENT DEPARTMENTKEY DEPARTMENTTITLE DESCRIPTION DOCUMENT EMPLOYEEDIMKEY
EMPLOYEEID EMPLOYEENAME ENTRY_DATE EXCHANGE_RATE EXCH_RATE_DATE EXCH_RATE_TYPE_ID
IETYPE ITEMDIMKEY ITEMID ITEMNAME LINE_NO LOCATION LOCATIONKEY LOCATIONNAME
MODIFIEDBY MODIFIEDBYLOGINID PARENTGLENTRYKEY PROJECTDIMKEY PROJECTID PROJECTNAME
RECON_DATE RECORDNO SELECTFORPBE STATE STATISTICAL TIMEPERIOD TMPLENTRYKEY
TRX_AMOUNT TR_TYPE UNITS USERNO VENDORDIMKEY VENDORID VENDORNAME WHENCREATED
WHENMODIFIED
```

Grouped by use:

- **Identity** — `RECORDNO`, `BATCHNO`, `LINE_NO`, `BATCH_NUMBER`, `STATE`, `TIMEPERIOD`
- **Account** — `ACCOUNTNO`, `ACCOUNTTITLE`, `ACCOUNTKEY`, `IETYPE`, `STATISTICAL`
- **Money** — `AMOUNT`, `TR_TYPE`, `TRX_AMOUNT`, `CURRENCY`, `BASECURR`, `EXCHANGE_RATE`, `UNITS`
- **Dates** — `ENTRY_DATE`, `BATCH_DATE`, `CLRDATE`, `RECON_DATE`, `EXCH_RATE_DATE`
- **Dimensions** — `LOCATION`, `DEPARTMENT`, `CLASSID`, `PROJECTID`, `CUSTOMERID`, `VENDORID`,
  `EMPLOYEEID`, `ITEMID`, each with a matching `*NAME`/`*TITLE` and `*KEY`
- **Audit** — `WHENCREATED`, `WHENMODIFIED`, `CREATEDBY`, `MODIFIEDBY`, `CREATEDBYLOGINID`

`GLENTRY` exposes both `WHENCREATED` and `WHENMODIFIED`, so watermark-based incremental sync is
possible later if volume ever justifies it.

## `GLDETAIL` field list (81)

```
ACCOUNTNO ACCOUNTTITLE ADJ AMOUNT AUCREATEDBY AUWHENCREATED BASECURR BATCHKEY
BATCH_DATE BATCH_NO BATCH_STATE BATCH_TITLE BOOKID CHILDENTITY CLASSDIMKEY CLASSID
CLASSNAME CLEARED CLRDATE CREATEDBY CREDITAMOUNT CURRENCY CUSTENTITY CUSTOMERDIMKEY
CUSTOMERID CUSTOMERNAME DEBITAMOUNT DEPARTMENTID DEPARTMENTTITLE DESCRIPTION
DOCNUMBER DOCUMENT EMPENTITY EMPLOYEEDIMKEY EMPLOYEEID EMPLOYEENAME ENTRYDESCRIPTION
ENTRY_DATE ENTRY_STATE FINANCIALENTITY GLENTRYKEY ITEMDIMKEY ITEMID ITEMNAME LINE_NO
LOCATIONID LOCATIONNAME LOCENTITY MODIFIED MODIFIEDBY MODULEKEY OWNERSHIPKEY
PRCLEARED PRCLRDATE PRDESCRIPTION PROJECTDIMKEY PROJECTID PROJECTNAME RECORDID
RECORDNO RECORDTYPE RECORDTYPE_ORIG REFERENCENO STATE STATISTICAL SYMBOL TOTALDUE
TOTALENTERED TOTALPAID TRX_AMOUNT TRX_CREDITAMOUNT TRX_DEBITAMOUNT TR_TYPE VENDENTITY
VENDORDIMKEY VENDORID VENDORNAME WHENCREATED WHENDUE WHENMODIFIED WHENPAID
```

## Which dimensions actually carry data

Sampled from GL entries dated after 01/01/2026:

| Dimension | Populated | Notes |
|---|---|---|
| `LOCATION` | ✅ 5/5 | The primary slice. |
| `VENDORID` | ✅ 5/5 | Purchase-side entries. |
| `ITEMID` | ✅ 5/5 | More used than expected — pull the `ITEM` master. |
| `DEPARTMENT` | ⬜ 0/5 | Only one department exists company-wide. |
| `CLASSID` | ⬜ 0/5 | Empty in sample. |
| `PROJECTID` | ⬜ 0/5 | Empty in sample. |
| `CUSTOMERID` | ⬜ 0/5 | Empty on GL entries; customers still exist as reference data. |

> ⚠️ **This is a signal, not a census.** Five rows from one query. The columns come across either
> way — this only indicates which are worth modelling. A non-null count across all ~175k rows is a
> Stage 1 deliverable.

## `GLACCOUNT` fields

```
ACCOUNTNO ACCOUNTTYPE ALTERNATIVEACCOUNT AUTOMATICACCOUNT CATEGORY CATEGORYKEY
CLOSETOACCTKEY CLOSINGACCOUNTNO CLOSINGACCOUNTTITLE CLOSINGTYPE CREATEDBY
CREATEDBYLOGINID ENABLE_GLMATCHING LETTRAGESEQUENCEID LETTRAGESEQUENCEKEY MEGAENTITYID
MEGAENTITYKEY MEGAENTITYNAME MODIFIEDBY MODIFIEDBYLOGINID MRCCODE NORMALBALANCE
RECLASSIFICATIONACCOUNTNO RECLASSIFICATIONACCOUNTTITLE RECLASSIFICATIONACCTKEY RECORDNO
REQUIRECLASS REQUIRECUSTOMER REQUIREDEPT REQUIREEMPLOYEE REQUIREITEM REQUIRELOC
REQUIREPROJECT REQUIREVENDOR SI_UUID STATUS SUBLEDGERCONTROLON TAXABLE TAXCODE TITLE
WHENCREATED WHENMODIFIED
```

The `REQUIRE*` flags are useful: they state which dimensions each account *requires*, which is a
second, independent read on which dimensions matter — worth cross-checking against the sampled
population above.

## How to reproduce any of this

No rebuild needed; `INTACCT_` env vars override `appsettings.json`:

```sh
# field list for an object
INTACCT_ReadObject=GLENTRY INTACCT_ReadFields='*' INTACCT_ReadQuery= \
  dotnet run --project samples/UmpaInvoiceService.Sample

# filtered read
INTACCT_ReadObject=GLENTRY INTACCT_ReadFields=RECORDNO,ENTRY_DATE,AMOUNT \
  INTACCT_ReadQuery="ENTRY_DATE > '01/01/2026'" \
  dotnet run --project samples/UmpaInvoiceService.Sample
```

The sample hardcodes `PageSize = 5` and does not print `totalcount`, which is why volumes here are
ranges rather than exact figures. Replacing it with a proper probe tool is Stage 1 of
`EXTRACTION-PLAN.md`.
