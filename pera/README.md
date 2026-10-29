# PERA Workflow

**Task Management, Document Movement, Approval & Digital Signature System**
Punjab Enforcement & Regulatory Authority — ADG Implementation

Built from the requirements set *PERA_Task_Document_Workflow* v0.1 (BRD, FRD, SRD, 29 October 2026) on
the same stack as SLIMS: a Next.js portal in front of a .NET 9 four-layer API over SQL Server.

    browser  ->  portal (Next.js :3002)  ->  API (.NET 9 :5092)  ->  SQL Server (database "Pera")
                                                  |
                                                  +->  uploads/ (document versions, evidence, signed copies)

The requirement documents are drafts. Everything that depends on a PERA decision — the real hierarchy,
the delegation of powers, the signature method, retention and integrations — is configuration or a
documented hook, not hard-coded. See **Decisions still needed** below.


## Run it

SQL Server Express must be running; the API creates the database, applies the migration and seeds on first start.

```bash
cd pera/backend/src/Pera.Api && dotnet run
```

```bash
cd pera/frontend && npm install && npm run dev
```

Open **http://localhost:3002**. Swagger is at **http://localhost:5092/swagger**.

No SQL Server on the machine (evaluation only): add `Database__Provider=Sqlite` and
`ConnectionStrings__Default="Data Source=pera.db"`. The schema is then created straight from the model,
with no migration path — never use this for live data.

Production settings, all outside source control (SR-SEC-04): `ConnectionStrings:Default`, `Jwt:Key`
(32+ characters), **`Signing:Key`** (32+ characters, the signature seal key), `App:UploadsDir`,
`App:SecureCookies=true` behind HTTPS, `App:SeedDemo=false`. DBAs who prefer scripts can apply
`backend/deploy/Pera-schema.sql` (idempotent).

### First sign-in

The database seeds an **illustrative** hierarchy with **no passwords**, like SLIMS. Sign in as
`sysadmin`; the password you type becomes the administrator password. Then issue temporary passwords
under **Settings ▸ Users & Access**; each officer chooses their own at first sign-in.

| User ID | Role | Designation · place in the hierarchy |
|---|---|---|
| `dg` | ADG / senior management | Director General (top) |
| `adg` | ADG / senior management | ADG Implementation, reports to `dg` |
| `dir-ops`, `dir-comp` | Manager | Directors, report to `adg` |
| `dd-ops` | Manager | Deputy Director Operations, reports to `dir-ops` |
| `ad-ops1`, `ad-ops2`, `so-ops` | Officer | report to `dd-ops` (`so-ops` is the dealing officer / initiator) |
| `ad-comp` | Officer | reports to `dir-comp` |
| `legal` | Officer | Legal Advisor (concurrence) |
| `rnd` | Officer | Receipt & Dispatch |
| `wfadmin` | Workflow Administrator | templates, document types, authority matrix |
| `audit` | Auditor (read-only) | the whole trail, all records |
| `sysadmin` | System Administrator | accounts and configuration — **no approval or signing power** |

Demonstration data: eight tasks (a parent with sub-tasks, one blocked, one escalated, one awaiting
closure review) and one draft summary with its PDF and route, ready for `so-ops` to submit.


## What it does

**Tasks.** ADG assigns to a manager; managers break work into linked sub-tasks for officers in their
reporting line (assignment outside the line is refused). Owners accept, update progress, record
blockers, attach evidence and submit; only the closure authority closes, returns or reopens — and not
while a sub-task is open, or (if set) while a linked document is unfinished. Reassignment needs a reason
and keeps the history. Comments carry the stage they were made at, replies and @mentions.

**Documents.** Register a case (unique number, type, classification, originating office, linked task),
upload versions (type, size, content signature and malware-scan hook checked; SHA-256 kept), and
propose the route from the type's template. The route is validated live against the template's
mandatory stages, the authority matrix, self-approval, inactive officers, signing-before-approval,
parallel and customisation rules; errors block submission, warnings need confirmation. On submission
the route is frozen as a snapshot and runs step by step — stages sharing a step run in parallel. Each
officer gets the action panel for their stage type: acknowledge, recommend, concur / not concur,
approve, reject, return for revision, sign, dispatch or place on record. Every action is checked on the
server against the stage, the officer (or their acting officer), the **exact active version** and, for
approval and signature, the authority matrix in force that day; refusals are written to the audit
trail. Returns send the case back to the initiator, who uploads a new version and resubmits; earlier
versions and comments stay. Information marking never counts as approval. After submission the route
changes only through an amendment with a reason and, where configured, approval by the requester's
reporting officer or a workflow administrator. Withdrawal is refused once an approval exists.

**Signatures** (controlled electronic signature — see the note below). The initiator places signature
boxes (page and normalised coordinates). The signer re-enters their password and confirms; the system
re-hashes the approved file, refuses if it changed, records signer, acting-for, method, time, the
document SHA-256 and a server HMAC seal, and stamps a **copy** of the PDF (the approved file is never
modified). The stamped copy becomes the active "Signed" version. **Verify** recomputes everything from the
file store; a code printed on the stamp can be checked under *Reporting ▸ Verify a Signature*. A material
revision after approval invalidates the signatures and the approvals and sends the case back for re-routing.

**Oversight.** Role-aware dashboard (work queue, task risk, aging, officer-wise status, document
bottlenecks by holder, stage turnaround, blocked tasks); approval and signature tracker; movement history
with time held per stage; reminders before due dates and two levels of escalation up the hierarchy;
in-app notifications with read state; eight standard reports with CSV/Excel export carrying the report
date and filters; an append-only audit trail with actor, acting-for, before/after, IP and request correlation.

**Visibility** (SR-SEC-02, SR-SEC-09). Officers see their own work and everything below them in the
hierarchy (plus the work of anyone they are acting for). Documents: *Public* to all; *Internal* and
*Confidential* to participants and their hierarchy; *Restricted* to participants and their acting officers only.
Auditors see everything, read-only.


## Requirement traceability (FRD section 2)

| FR | Where |
|---|---|
| 001 Identity & access | `Core.cs` (JWT, lockout, roles), visibility in `TaskService` / `DocumentService` |
| 002 Organisation hierarchy | `Hierarchy.cs`, Settings ▸ Hierarchy, Users & Access (reporting officer) |
| 003–008 Tasks | `TaskService.cs`, `Rules.cs` (`TaskLifecycle`), Tasks page |
| 009–011 Registration, upload, versions | `DocumentService` (register, versions, logged preview/download), `FileService` |
| 012 Templates | `WorkflowAdminService` (editing an active template publishes the next version) |
| 013 Initiator routing | `DocumentService.SetRouteAsync`, `RouteValidator` |
| 014 Route amendment | `DocumentService.RequestAmendmentAsync` / `DecideAmendmentAsync` |
| 015–017 Review, information, approval | `DocumentService.ActAsync` / `CheckStageAsync` |
| 018–019 Signature placement and signing | `SetSignatureFieldsAsync`, `SignAsync`, `VerifyAsync`, `PdfStamper` |
| 020 Movement history | `Movements` table, Movement tab, movement report |
| 021 Dashboards | `DashboardService`, Dashboard and Approvals pages |
| 022 Search & filters | list endpoints and page filters |
| 023 Notifications | `NotificationService` (in-app, read state) |
| 024 Escalation | `AutomationService` + `AutomationWorker`, `EscalationLogs` |
| 025 Audit | `AuditService` (append-only; refusals committed before the error is returned) |
| 026 Reports & export | `ReportService`, `XlsxReportWriter` (ClosedXML) |
| 027 Delegation | `Delegation` entity, Settings ▸ Acting Officers; actions record acting-for |
| 028 Administration | Parameters, document types, categories, designations, units — all audited |

`backend/tests/Pera.Tests` runs the nine FRD section 7 acceptance scenarios end to end through the real
services (plus delegation, restricted visibility, amendments, information marking, upload checks,
withdrawal and revision), and unit tests for the route validator, task lifecycle and seal.

```bash
cd pera/backend && dotnet test
```


## Stack

Same as SLIMS: **Next.js 16.3.5** (App Router, standalone), **React 19.2.8**, **TypeScript 5**, hand-written
CSS with the SLIMS/Odoo palette and inline-SVG charts (no UI or chart library); **.NET 9**, C#, **EF Core 9.0.7**
code-first with startup migration, **JWT** bearer + httpOnly refresh cookie, Swagger, a background
worker; **SQL Server 2022 Express**; files on disk with hashes and paths in the database; **ClosedXML** for
Excel. One addition: **PDFsharp 6** (MIT) to stamp signature blocks on PDFs. Four layers as in SLIMS:
Domain (entities, status machines, route rules) → Application (services, DTOs) → Infrastructure (EF,
storage, PDF/Excel, identity) → API (controllers, worker).


## Decisions still needed (BRD 11, SRD 12)

1. **Signature method.** What is built is a *controlled electronic signature*: password re-authentication,
   SHA-256 binding to the approved version, an HMAC seal under a server key and a visible stamp. It is
   **not** a certificate-based digital signature and the system says so on screen. If PERA legal requires
   PKI signatures for some categories, the signing step is the integration point for the approved provider.
2. **Hierarchy and powers.** The seeded hierarchy, designations and authority matrix are illustrative;
   load the approved ones (Settings ▸ Hierarchy, Workflow Setup ▸ Authority matrix) and set `App:SeedDemo=false`.
3. **Malware scanning.** `IFileScanner` is a hook; the default logs that no scanner is configured.
4. **Notifications.** In-app only; e-mail/SMS wait for an approved provider (BRD 8).
5. **Identity.** Local accounts with PBKDF2 and lockout; SSO/MFA and HRMIS sync are integration points (SRD 9).
6. **Formats.** In-browser preview and signature stamping cover PDF (and images for preview). Office files
   upload, version and route normally but are signed without a visible stamp.
7. **Retention, RTO/RPO, hosting** — to be set with PERA IT before production.
