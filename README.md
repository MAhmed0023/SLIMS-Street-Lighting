# SLIMS

**Street Lighting Store, Issuance & Installation Management System**
Government Street Lighting Project — Project Director's Office

QR-based store control, digital requisition and issuance, and geo-verified installation evidence
for street lighting material. Built on the same stack as PIMRS: Next.js portal in front of a
.NET 9 Clean Architecture API over SQL Server, plus the **SLIMS Field** Android app that
Lighting Officers use at the pole.

- Specification — [docs/SLIMS_Business_Requirements_Document.docx](docs/SLIMS_Business_Requirements_Document.docx)
- Process flow — [docs/SLIMS_Process_Flow.jpg](docs/SLIMS_Process_Flow.jpg)
- Gap analysis against FRD-GSL-001 — [docs/SLIMS_Gap_Analysis_FRD_vs_Build.xlsx](docs/SLIMS_Gap_Analysis_FRD_vs_Build.xlsx)
- Technical stack — [STACK.md](STACK.md)

This repository also holds **PERA Workflow** — task management, document movement, approval and signature
for the Punjab Enforcement & Regulatory Authority — a separate application on the same stack, in
[pera/](pera/README.md).


## Run it

Two terminals. SQL Server Express must be running; the API creates and seeds the database on first
start.

```bash
cd backend/src/Slims.Api && dotnet run
```

```bash
cd frontend && npm install && npm run dev
```

Then open **http://localhost:3001**. The API's Swagger page is at **http://localhost:5091/swagger**.

### First sign-in

The database seeds twelve accounts with **no passwords**. Sign in as `sysadmin` and the password
you type becomes the administrator password. From **Settings ▸ Users & Access** you then issue a
temporary password to each of the others; they choose their own on first sign-in.

| User ID | Role | What they can do |
|---|---|---|
| `sysadmin` | System Administrator | everything, including users and parameters |
| `pd` | PD Office / MIS | approve, verify, reports, parameters |
| `sic-cen` | Store In-Charge | receive, approve requisitions, issue, returns |
| `sk-cen` | Store Keeper | receive, issue, returns |
| `sup-central` / `-north` / `-south` | Lighting Supervisor | sites, requisitions, verify exceptions |
| `off-central` / `-north` / `-south` | Lighting Officer | installation evidence, report failures |
| `proc` | Procurement | purchase orders, vendors, discrepancies, warranty |
| `audit` | Internal Audit | read everything, export |

Seeded demonstration data: 2 stores with bins, 7 geo-fenced sites across 3 zones, 2 vendors,
2 purchase orders, 6 articles and 298 QR labels, of which 218 are already received into stock
against two opening GRNs.


## Deployment

| What | How |
|---|---|
| Live on this PC, office LAN | `deploy\start_slims.pyw` runs the production build at `http://<this PC's IP>:3001`. `--watch` keeps it alive, `--stop` stops it. After a code change run `deploy\publish-local.bat` |
| Another PC or a server | `package-for-another-pc.bat` builds `SLIMS-Setup`. Copy it across and follow [INSTALL.md](INSTALL.md) |
| Public host behind HTTPS | `python make-release-zips.py` builds `release\SLIMS-frontend-*.zip` and `SLIMS-backend-*.zip`, each with its own README |

Ports: web 3001 (open on the LAN), API 5091 (127.0.0.1 only). PIMRS keeps 3000 and 5090.

The API also runs `AutomationWorker` in the background: approval escalation, requisition lapse,
reorder and exception alerts, and scheduled report delivery. E-mail and SMS stay off until they
are filled in on **Settings ▸ Parameters ▸ Notification channels**; until then alerts go to the
in-app bell only.


## SLIMS Field — the phone app

Mobile setup is **mandatory**: a user with the installation permission cannot use the portal until
one of their phones is set up and approved, and the portal refuses web-captured installation
evidence (`FieldSettings.AllowWebCapture = false`).

1. Portal **Settings ▸ Mobile Setup**. Scan the first QR code with the phone to download
   `slims-field.apk` and install it (allow "install unknown apps" once).
2. Open SLIMS Field and scan the **setup code** shown on the same page (valid 15 minutes).
3. Allow camera and location. The app checks GPS, mock location and automatic time.
4. Take the **setup photograph** outdoors with the live camera. The phone is registered to that
   user; an administrator approves it under **Settings ▸ Users & Access ▸ Field phones**.

The app captures photographs only from the live camera (no gallery), reads GPS natively with
Android's mock-location flag, hashes each photograph (SHA-256) before upload, and keeps work in an
offline queue that synchronises when the network returns. The server stamps its own evidence copy.

Build it with `mobile\build-apk.bat` (free JDK 21 + Android SDK in `C:\Users\User\android-dev`);
it publishes the APK to `deploy\api\downloads`, from where phones download it. **Back up
`mobile\keystore`** — every update must be signed with the same key or phones cannot upgrade.


## The process, end to end

1. **Procurement inward.** An article is created against a purchase order line, item-code wise
   (`LUM-LED-090W-PO2611-001`), and its QR series is generated and printed. The Store Keeper scans
   each label at the inward gate; the scan is matched to the open PO line, and the GRN is posted
   to a specific bin. A short or excess receipt raises a discrepancy note for Procurement.
2. **Digital requisition.** The Lighting Supervisor raises a requisition against a site drawn from
   the site master. The system checks availability, raises an automatic purchase indent for any
   shortfall, and routes the requisition for approval. Approval reserves the stock.
3. **Issuance.** The Store Keeper scans each item against the approved requisition. Custody moves
   from the store bin to the site, and a gate pass and issue note are numbered.
4. **Installation.** The Lighting Officer captures a BEFORE photograph, scans the item at the pole,
   and captures an AFTER photograph. Coordinates and time are burned onto the images at the moment
   of capture. The server measures the distance to the site: inside the geo-fence it confirms the
   installation, outside it raises an exception for the Supervisor to verify in writing.
   The officer records this in the SLIMS Field app; the portal views it (stamped copy,
   drill-down, map) and can request a field verification.
5. **Returns.** Failed items are reported at site, scanned back into a quarantine bin, and routed
   to warranty, repair or condemnation. Warranty eligibility is computed from the installation date
   and the warranty period carried on the item code.

Around these steps sit the store controls — put-away and bin moves, store-to-store transfers,
stock adjustments with a second approver, physical counts with reconciliation, a quantity ledger —
plus amendment requests, notifications with escalation, the explore drill-down and the map.

Every report in **Reporting ▸ Reports** is generated from these transactions, with no manual
compilation.


## Verify it works

With both servers running:

```bash
cd backend && python e2e_check.py
```

68 checks walk one item through the whole chain and assert that each control refuses what it is
supposed to refuse — a forged QR payload, issuing before approval, issuing beyond the approved
quantity, installing without the AFTER photograph, approving a deviation without a justification,
approving your own requisition, verifying your own installation, disposing an item that has not
been condemned. The script needs Pillow (`pip install pillow`) to make the test photographs.

`python frd_acceptance.py <portal-url> --db <database>` runs the 30 FRD acceptance test cases plus
the requirement and defect probes (57 checks); results go to `frd_acceptance_results.json`.

**It writes real data** into whatever database the API is pointed at, so run it against a
development copy only. To start clean:

```bash
cd backend && dotnet ef database drop -f --project src/Slims.Infrastructure --startup-project src/Slims.Api
```


## Repository layout

    backend/          .NET 9 solution — Domain, Application, Infrastructure, Api
      e2e_check.py    end-to-end business rule check against a running API
      frd_acceptance.py  FRD Section 21 acceptance run
    frontend/         Next.js portal
    mobile/           SLIMS Field Android app (Capacitor + React), build-apk.bat
    deploy/           live launcher, publish and admin scripts
    docs/             BRD, process flow, gap analysis
    STACK.md          technical stack and where each rule is enforced
