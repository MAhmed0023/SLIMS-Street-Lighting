# SLIMS — server requirements for PITB

Street Lighting Store, Issuance & Installation Management System
Prepared for the **Municipal Town Admin Office** · 06 October 2026

The system is a web portal and API used by store and field staff, a SQL Server database, a folder of
geo-tagged installation photographs, and an Android app (SLIMS Field) used by Lighting Officers at
the pole. Two virtual machines are requested: one web/application server and one database server.

---

## 1. Web / application server

| | Minimum | Requested |
|---|---|---|
| vCPU | 4 | **8** |
| RAM | 8 GB | **16 GB** |
| Storage (OS + data, SSD) | 250 GB | **500 GB, expandable** |
| Live (public) IP | 1 | **2** |
| Operating system | Windows Server 2019 Standard ×64 | Windows Server 2022 Standard ×64 |

The installation photographs are stored as files on this server, so the 500 GB must be expandable later
without rebuilding the VM. Two public addresses are requested: one for the service, one held for
failover or a second site.

## 2. Database server

| | Minimum | Requested |
|---|---|---|
| vCPU | 4 | **12** |
| RAM | 8 GB | **16 GB** |
| Storage (OS + data + backups, SSD) | 100 GB | **200 GB, expandable** |
| Live (public) IP | None — private IP only | None — private IP only |
| Operating system | Windows Server 2019 Standard ×64 | Windows Server 2022 Standard ×64 |

SQL Server **Standard** edition is requested: the free Express edition uses only about 1.4 GB of
memory and four cores however large the VM is, so this specification pays off only on Standard.

---

## 3. Storage — how it was calculated

Photographs are the only part that grows. Each installation carries a BEFORE and an AFTER
photograph (1920 × 1080 JPEG), and the server keeps its own stamped copy of each — about 2.5 MB per
installation in total.

| Installations recorded | Photograph storage on the web server |
|---|---|
| 10,000 | ~25 GB |
| 50,000 | ~125 GB |
| 100,000 | ~250 GB |

At this rate **500 GB holds about 200,000 installations**, and the disk can be grown later. The database stays small: about **1 GB per
100,000 items**, because photographs are files on disk, not rows in the database. The 200 GB on the database server covers the data, the transaction log and local backups for many years.

---

## 4. Bandwidth

| | |
|---|---|
| Sustained | 10 Mbps minimum · **20–30 Mbps recommended** |
| Burst | 50 Mbps |
| Monthly transfer | ~20–25 GB (≈ 7 GB of photograph uploads per 5,000 installations, plus portal and report traffic) |
| Peak pattern | Field uploads: each installation sends ~1.4 MB. 20 officers uploading at the same time ≈ 20 Mbps inbound. Portal users are light (~2 Mbps for 30 concurrent users) |

---

## 5. Operating system and software required on each server

**Web / application server**

| Item | Note |
|---|---|
| Windows Server 2019 / 2022 Standard ×64 | As above |
| Node.js 20 LTS or newer | Runs the portal. The deployment package can carry its own copy if installation is not permitted |
| IIS with URL Rewrite + ARR, **or** Nginx for Windows | Reverse proxy, terminates HTTPS on 443 |
| .NET runtime | **Not required** — the API is published self-contained (win-x64) |
| Antivirus exclusion | The uploads folder, or evidence uploads slow down badly |

**Database server**

| Item | Note |
|---|---|
| Windows Server 2019 / 2022 Standard ×64 | As above |
| SQL Server 2019 or 2022 | **Express edition is acceptable** (free; 10 GB per database is years of data at this volume). Standard preferred if a licence is already available |
| SQL Server Management Studio | For the database administrator |
| Antivirus exclusion | SQL data and log folders |
| Mixed-mode authentication | One SQL login for the application, db_owner on the SLIMS database only |

**Both servers:** time synchronisation (NTP) must be enabled — installation photographs are checked
against server time, and a drifting clock causes captures to be refused.

---

## 6. Network and ports

| Direction | Port | Purpose |
|---|---|---|
| Internet / PERA network → web server | 443 (HTTPS) | The only port users and phones need. HTTPS is mandatory: the Android app and the browser's GPS both refuse plain HTTP |
| Internet → web server | 80 | Redirect to 443 only |
| Admin network → both servers | 3389 (RDP) | Deployment and maintenance, restricted to named addresses |
| Web server → database server | 1433 (TCP), private network only | Application to database |
| Web server → outside | 587 or 25 (SMTP) | Alert and report e-mail; an internal relay is fine |
| Web server → outside | 443 | SMS gateway when enabled, Windows Update |

Portal (3001) and API (5091) listen on 127.0.0.1 behind the reverse proxy and are never exposed.

**Why the public addresses are needed:** Lighting Officers work in the field, so the web server must
be reachable over the internet — two public addresses, a DNS record and an SSL certificate. No VPN
is required on the phones; the Android app connects straight to the published address over HTTPS.

---

## 7. Domain name and certificate

| | |
|---|---|
| Hostname | e.g. `streetlight.pera.gop.pk`, or a name issued by PITB |
| SSL certificate | Valid certificate for that name, renewal handled by PITB |
| Why it matters | HTTPS is required for GPS in the browser, for the Android app's connection, and for the APK download link on the portal |

---

## 8. Accounts, backup and recovery

- One local administrator account per server for installation and updates
- One service account on the web server to run the two Windows services, with "log on as a service"
- Database: daily full backup, retained 30 days · photographs folder: daily incremental, retained 30 days · weekly VM snapshot of both servers, retained 4 weeks
- Restore tested once per quarter · RPO 24 hours, RTO 4 hours requested

---

## 9. What we provide, and what is not needed

**Provided by us:** the deployment package (API, portal, Node runtime, install guide), the
deployment itself, database creation and seeding (the application creates its own schema on first
start), the signed Android APK served from the portal, configuration, user accounts, and handover
to the MIS staff.

**Not required:** any public cloud service; any paid licence beyond Windows Server and, optionally,
SQL Server; a .NET runtime installation; a load balancer, container platform or separate
application server at this size.
