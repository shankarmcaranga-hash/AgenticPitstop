# Agentic PITSTOP — ASP.NET Core MVC portal

Configuration-driven human-in-the-loop review portal. One instance and one database
per customer; nothing in the code is aware of more than one tenant.

This is the Phase 2 build of the Phase 1 static prototype. The visual system,
screen flow and validation rules are carried over; state moved from `localStorage`
to Azure SQL, and role switching moved from a dropdown to the Windows identity.

---

## 1. Constraints this build honours

| Constraint | How |
|---|---|
| No Entity Framework / ORM | `Microsoft.Data.SqlClient` only |
| Stored procedures for **all** DB work, including SELECT | `ApsDb` has no method that accepts SQL text |
| No model classes | `DataTable` / `DataRow` + `ViewBag`; JSON for AJAX payloads |
| Views and Controllers only | No Razor Pages, no minimal APIs |
| No CDN, no outside dependencies | jQuery and Font Awesome vendored into `wwwroot/lib`; `.xlsx` parsed with the BCL |
| Windows Authentication | Negotiate (Kestrel) + IIS handler; SSO swaps in at one place |
| RBAC — Admin / Manager / Associate | `[ApsAuthorize]` filter on every action |
| `APS_` prefix on every table and procedure | Enforced throughout `/Database` |
| .NET 8, forward-compatible with 10 | Single `TargetFramework` property; no APIs removed after 8 |

---

## 2. Getting it running

### Database

Run in order against your Azure SQL database:

```
Database/01_APS_Tables.sql
Database/02_APS_StoredProcedures.sql
Database/03_APS_SeedData.sql
```

**Before running the seed**, change the `portalid` values in section 1 to real
Windows usernames — at minimum, change `prasanna` to your own. `portalid` holds
the username *without* the domain. If no row matches your Windows identity you
will land on the "account not set up" page and cannot reach any screen.

The seed also needs `CREATE TABLE` and `ALTER TABLE` permission, because
`APS_InflowData_Rebuild` and `APS_Collection_Save` generate tables.

### Application

1. Set the connection string in `appsettings.json` (or user secrets — preferred,
   the id is already set to `agentic-pitstop-portal`).
2. `dotnet run`, or publish to IIS with `web.config` as supplied.
3. Anonymous authentication must be **off** in IIS. Windows authentication on.

For a path-based deployment (`https://my.agenticpitstop.com/customerabc`), set
`Aps:PathBase` to `/customerabc` and point that application at that customer's
database.

---

## 3. Where the configurability lives

Four things shape the portal, and none of them is code:

| Configured in | Stored in | Changes |
|---|---|---|
| Inflow Source | `APS_Configuration`, `APS_ConfigHeader`, `APS_InflowColumn` | how work arrives; the shape of `APS_InflowData` |
| Collections | `APS_Collection_Master`, `APS_Collection_Field` | generated `APS_Collection_<Name>` tables |
| Inbox Setup | `APS_InboxConfig` | which columns the Inbox shows, in what order |
| Detail Setup | `APS_DetailConfig`, `APS_DetailTab`, `APS_DetailField` | the whole review screen |

Adding a **screen** is the one thing that needs code: one entry in
`ApsMetadata.Screens`, a controller action, a view. Everything else is a row.

### Detail layout model

Sub-tabs are rows in `APS_DetailTab` with a non-null `parenttabid` — a
self-reference rather than a second table. Header fields are rows in
`APS_DetailField` with a null `dtid`.

`mode` (1 = Data Review, 2 = Evidence Review) is on both tables, and saves are
scoped by mode. Switching workspace type therefore keeps the layout you are not
looking at, which the prototype discarded.

---

## 4. Dynamic SQL — where it is and why

Two procedures emit DDL, and only two:

- `APS_InflowData_Rebuild` — reshapes `APS_InflowData` to the uploaded template
- `APS_Collection_Save` — creates or extends `APS_Collection_<Name>`

Both validate every identifier against `^[A-Za-z][A-Za-z0-9_]*$`, reject reserved
column names, then wrap with `QUOTENAME`. The application sends a name and a field
list; it never sends SQL text.

There is one deliberate exception on the app side: `InflowController.InsertRows`
uses `SqlBulkCopy` against `APS_InflowData`. That table has no fixed column list —
its shape comes from the customer's template — so no procedure signature can
describe it. Column names come from `APS_InflowColumn` (already validated by
`APS_InflowData_Rebuild`) and are mapped by name, not concatenated.

---

## 5. Things that changed from the prototype, deliberately

**The role switcher is gone.** In Phase 1 it was a review aid. Shipped as-is it
would have let any Associate promote themselves to Admin. Role now comes from
`APS_Users` via the Windows identity, and `[ApsAuthorize]` is checked server-side
on every action — sidebar visibility is a courtesy, not a boundary.

**Header secrets never round-trip.** Values are masked in `APS_ConfigHeader_Get`
and only released by an explicit `RevealHeader` call. On save, an unrevealed
header sends its `chid` instead of the mask, and the server carries the stored
value forward — otherwise saving would overwrite real credentials with asterisks.

**`Test API` runs server-side** using the *saved* endpoint, body and headers, not
the form values. Testing from the browser would expose credentials and be blocked
by CORS anyway.

**The document viewer states its own absence.** No document store is wired up
yet, so the `documents` display type says so rather than rendering placeholder
pages that look like a working viewer.

---

## 6. Not built

- **Dynamic Reports** — structural placeholder only. No report definition model,
  grouping or export format has been agreed.
- **The agent runner.** The portal stores agent definitions and schedules and
  reads execution history. Nothing here actually invokes an agent on a timer;
  that runner reports in through `APS_AgentExecution_Log`.
- **Automated API inflow.** `APS_Configuration` holds the endpoint and schedule,
  and `Test API` proves the endpoint works, but no scheduled job pulls from it
  yet. Manual upload is the working path today.
- **Document/image storage** for the Evidence Review workspace.
- **Outlook `.msg` and CSV inflow** — Excel only, as specified.

## 7. Known gaps to check on first build

This project has never been compiled — there was no .NET SDK available when it
was written. Expect a first-build pass to surface a few Razor or signature
issues. Worth checking first:

- `@functions` blocks in `Views/Inbox/Index.cshtml` and `Detail.cshtml`
- `ViewDataDictionary` construction in `Detail.cshtml` when passing to
  `_DetailContent`
- Nullable `byte?` binding on `ConfigurationController.SaveInflow`
- TVP type names matching exactly (`dbo.APS_ColumnListType` etc.)

### Verified before hand-off

These were checked mechanically, not by compiling:

- every `APS_` procedure called from C# exists in `02_APS_StoredProcedures.sql`
- every `Url.Action` target resolves to a real controller action
- every `ViewBag` key read in a view is set by a controller
- all inline `<script>` blocks parse as valid JavaScript
- HTML tags balance in every view; Razor braces balance in every view
- `BEGIN`/`END`, `TRY`/`CATCH` and transaction blocks balance in the SQL
- no table or procedure is missing the `APS_` prefix
- no CDN or external asset reference anywhere
