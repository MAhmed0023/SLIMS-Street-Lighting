# SLIMS — application stack

Street Lighting Store, Issuance & Installation Management System
Government Street Lighting Project — Project Director's Office

Two servers, the same shape as PIMRS. The browser only ever talks to the portal; the portal
forwards `/api` to the API server side. That keeps the session cookie first-party and means no
CORS is involved.

    browser  ->  portal (Next.js :3001)  ->  API (.NET 9 :5091)  ->  SQL Server
                                                  |
                                                  +->  uploads folder (evidence photographs)


## What the system is for

Every item of street lighting material carries a unique QR code created at the moment the article
is set up against a purchase order. Nothing enters stock, leaves store, reaches a site or comes
back without a scan, and an installation is only accepted when before-and-after photographs —
stamped with coordinates and time at the instant of capture — fall inside the geo-fence of the site
the material was issued for. The full specification is the BRD in `docs/`.


## Frontend — the portal

| | |
|---|---|
| Framework | Next.js 16.3.5, App Router |
| UI library | React 19.2.8 |
| Language | TypeScript 5 |
| Runtime | Node.js 20 or newer (built and tested on 24.18.0) |
| Styling | Hand-written CSS with custom properties, Odoo palette. No Tailwind, no component library. |
| Fonts | Inter, IBM Plex Mono (Google Fonts) |
| Charts | Hand-written inline SVG. No charting dependency. |
| State | React hooks and context. No Redux or similar. |
| Build output | `output: "standalone"` — a self-contained server that runs with `node server.js` |
| Lint | ESLint 9 with `eslint-config-next` |

**Runtime dependencies: three.** `next`, `react`, `react-dom`. Nothing else ships to the server.

Source layout:

    app/(app)/…        one folder per page: dashboard, inward, stock, articles, requisitions,
                       issue, field, returns, purchase-orders, vendors, sites, stores,
                       reports, audit, access, parameters
    app/login/         the sign-in screen
    components/        Shell, Scan panel, dialogs, charts, shared controls
    lib/               API client, auth, formatting, types
    proxy.ts           strips forged client-address headers before the API sees them
    scripts/           standalone asset copy after build

30 TypeScript files.

### Colour scheme

Odoo: plum `#714B67` for the global bar and primary actions, teal `#017E84` for links and
secondary emphasis, light grey ground with white sheets and subtle rules. Every colour is a CSS
custom property on `:root`, redefined for dark mode both under `prefers-color-scheme` and under an
explicit `[data-theme="dark"]` that the header toggle sets.


## Backend — the API

| | |
|---|---|
| Platform | .NET 9 (SDK 9.0.316), ASP.NET Core Web API |
| Architecture | Clean Architecture — Domain, Application, Infrastructure, Api |
| ORM | Entity Framework Core 9.0.7, code-first migrations |
| Database provider | `Microsoft.EntityFrameworkCore.SqlServer` 9.0.7 |
| Authentication | `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.7 |
| QR generation | QRCoder 1.6.0 (model 2, error correction M) |
| API documentation | Swashbuckle.AspNetCore 7.2.0 (Swagger at `/swagger`) |
| Web server | Kestrel on 0.0.0.0:5091, behind IIS or Nginx |
| Deployment | Self-contained win-x64 — no .NET runtime needed on the server |

Four projects, dependencies pointing inwards only:

    Slims.Domain           entities, item lifecycle, geo-fence maths, item-code and QR rules,
                           access policy. No dependencies at all.
    Slims.Application      services, DTOs, use cases. Depends on Domain.
    Slims.Infrastructure   EF Core, SQL Server, password hashing, file storage, QR rendering.
    Slims.Api              controllers, JWT setup, composition root.

29 C# files plus the Initial migration.

### Where the rules live

| Rule | Enforced in |
|---|---|
| Item status transitions | `Domain/Rules/Rules.cs` — `ItemLifecycle` |
| Item code and QR payload, check value, financial year | `Domain/Rules/Rules.cs` — `Codes` |
| Geo-fence distance | `Domain/Rules/Rules.cs` — `Geo` |
| Permission, zone, store and site scope | `Domain/Rules/Rules.cs` — `AccessPolicy` |
| Receipt against PO, tolerance, binning | `Application/Services/GrnService.cs` |
| Site-bound requisition, reservation, approval | `Application/Services/RequisitionService.cs` |
| Scan-based issue, approved quantity, custody | `Application/Services/IssueService.cs` |
| Evidence, geo-validation, exception approval | `Application/Services/InstallationService.cs` |
| Quarantine, warranty, repair, condemnation | `Application/Services/ReturnService.cs` |

Every refusal carries the BRD rule reference (`BR-013`, `FR-GR-03` …) in the `step` field of the
error response, and the portal shows it to the user.


## Database

| | |
|---|---|
| Engine | Microsoft SQL Server (SQLEXPRESS in development) |
| Schema | Created and versioned by EF Core migrations, applied on API start |
| Main tables | Items, Articles, PurchaseOrders, PurchaseOrderLines, Vendors, Stores, StoreLocations, Sites, Grns, DiscrepancyNotes, Requisitions, RequisitionLines, Issues, Installations, Evidence, ReturnNotes, Users, Roles, AuditLogs, AppSettings, DocSequences |

`Items` is the centre of the model: one row per QR label, carrying its own status, its current
store location or site, and the purchase order it arrived on.

Evidence photographs are not stored in the database. They live on disk under `App:UploadsDir`,
with the row in `Evidence` holding the path, the coordinates, the capture time and the uploader.
**Back up the database and that folder together** — either one alone is incomplete.


## Security

| | |
|---|---|
| Sign-in | User ID or official email, plus password |
| Password storage | PBKDF2-SHA256, 120,000 iterations, 16-byte salt, 32-byte hash |
| Session | JWT access token (60 min) in memory, rotating refresh token (7 days) in an httpOnly cookie |
| Transport | HTTPS at both hosts in production; `SecureCookies` true so the refresh cookie never leaves TLS |
| Authorisation | Role permission × zone × assigned stores × assigned sites, checked on the server for every write |
| Audit | Append-only `AuditLogs`. Every scan, decision and master change records who, what, when, from where and why. |

Access control is enforced in the API, not by hiding buttons. The pipeline is: identity, then
permission, then data scope, then item status, then the business rule. Deny by default at each
step. No posted transaction can be deleted — corrections are reversals, and both entries stay
visible.


## Build and deployment

| | |
|---|---|
| Portal build | `npm run build` — produces `.next/standalone`, with static assets copied in |
| API build | `dotnet publish -c Release -r win-x64 --self-contained true` |
| Portal hosting | Node process registered as a Windows scheduled task, behind IIS or Nginx |
| API hosting | IIS with the ASP.NET Core Module, or a Windows service |

The API address is fixed inside the portal build: Next resolves its rewrites when it compiles, not
when it starts. Moving the API means editing `API_URL` in `.env.production` and rebuilding.


## Office LAN install

The same two pieces run on one laptop, with the portal pointing at `http://localhost:5091` and SQL
Server Express on the same machine. That copy is independent of any server.
