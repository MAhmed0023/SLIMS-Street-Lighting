# Installing SLIMS on another machine

**SLIMS: Street Lighting Store, Issuance & Installation Management System**, for the Government
Street Lighting Project.

The same package works on an office PC and on a server or VM. Only the way you start it differs.

---

## Do you need to install anything?

**No, if the person only needs to use SLIMS.** SLIMS runs on one machine. Everybody else opens it
in a browser:

```
http://<the machine's IP>:3001
```

There is nothing to install on their PC. Chrome, Edge or Firefox is enough. Lighting Officers
capturing installation evidence can use the browser on their phone, on the office Wi-Fi. Each
person needs a user account, which the administrator issues under **Settings ▸ Users & Access**.

Install a second copy only when SLIMS itself has to *run* on that machine, for example a spare PC,
a server or a VM.

> SLIMS uses ports **3001** (web) and **5091** (API). The construction monitoring app (PIMRS) uses
> 3000 and 5090, so both can run on the same machine.

---

## What the machine that runs SLIMS needs

| | Required | Notes |
|---|---|---|
| Windows | 10, 11, or Windows Server 2019+ | 64-bit |
| **SQL Server Express** | **Yes. This is the only real prerequisite** | Free. Install it with the default instance name `SQLEXPRESS` |
| .NET runtime | No | The package carries its own copy |
| Node.js | No | The package carries `node.exe` |
| Disk / RAM | 2 GB free, 4 GB RAM | Evidence photographs need more disk over time |

Download SQL Server Express from Microsoft and choose the **Basic** installation. Keep the default
instance name `SQLEXPRESS`. Nothing else needs configuring: SLIMS creates its own database, called
`Slims`, the first time it starts.

---

## Step 1: Make the package (on the development machine)

Double-click:

```
slims\package-for-another-pc.bat
```

It produces a folder called **`SLIMS-Setup`**. Copy the whole folder to the other machine, for
example to `C:\SLIMS`. A USB stick or a network share is fine.

---

## Step 2a: Start it on an ordinary PC

Double-click **`START-SLIMS.bat`**.

The script:

- checks SQL Server;
- on the first start, writes this machine's own settings with a fresh signing key;
- starts both parts;
- prints the address for the office;
- opens the browser.

The first start takes about a minute, because the database and its tables are created and the
demonstration master data is loaded.

Keep the window open while people are using SLIMS. **`STOP-SLIMS.bat`** stops it.

To have SLIMS start by itself when the PC is switched on, put a shortcut to `START-SLIMS.bat` in:

```
shell:startup
```

## Step 2b: Start it on a server or VM

On a server, SLIMS must run with nobody logged in, so install it as a service. First copy the folder
outside any user profile, for example `C:\SLIMS`, because the service account cannot read files
under `C:\Users\...`. Then right-click this file and choose **Run as administrator**:

```
3-install-as-service-RUN-AS-ADMIN.bat
```

The script then:

- creates the **`SlimsApi`** Windows service, which restarts itself if it ever stops;
- creates the **`SLIMS Web`** startup task;
- gives the service account (`NETWORK SERVICE`) access to SQL Server Express and to the `uploads`
  folder.

Both parts start at boot.

---

## Step 3: Let the office in

Right-click this file and choose **Run as administrator**:

```
1-open-firewall-RUN-AS-ADMIN.bat
```

It opens port 3001 on the private and domain networks only, then prints the address to share.
Port 5091 stays closed: the API listens only on `127.0.0.1`, and only the web app talks to it.

If the office network is marked **Public** in Windows, the rule does not apply there, and the
script says so. Ask IT to mark the network as Private or Domain.

The machine must keep the same IP address, or everyone's bookmark breaks. Run
`2-set-fixed-ip-RUN-AS-ADMIN.bat` to read the MAC address, then ask the network administrator for a
DHCP reservation. That script only reads settings; it changes nothing.

---

## Step 4: First sign-in

Open the address and sign in as **`sysadmin`**. A fresh installation has no passwords yet, so the
password you type the first time becomes the administrator password. It must be at least 8
characters and contain both letters and digits.

Then open **Settings ▸ Users & Access** and issue a temporary password to each person: the PD
Office, Store In-Charge, Store Keepers, Lighting Supervisors, Lighting Officers, Procurement and
Audit. Each person chooses their own password at their first sign-in.

---

## Step 5: Lighting Officers set up their phones

Installation photographs come only from the **SLIMS Field** Android app, on the officer's own
registered phone. The app uses the live camera only (there is no gallery), records a real GPS fix,
detects mock-location apps and works offline. A browser upload is refused unless
**Parameters ▸ Mobile field app** allows it.

Mobile setup is mandatory for Lighting Officers. Until it is done, the portal shows them only the
Mobile Setup page. Each officer does this once, with their phone and a computer:

1. On the computer, sign in and open **Settings ▸ Mobile Setup**.
2. Scan the first code with the phone's camera to download the app (`SLIMS-Field.apk`), then allow
   the installation. Android 10 or newer is required.
3. Open the app, tap **Scan setup code** and scan the second code shown on the page. The code is
   valid for 15 minutes and works once. The phone never stores the officer's password.
4. Outdoors, allow camera and location. When every check is green, take the setup photograph.

A second phone for the same person waits for the System Administrator's approval under
**Users & Access ▸ Field phones**. Approving it retires the old phone. The administrator can also
revoke a lost phone there.

The app is served by the API from `api\downloads\slims-field.apk`. To rebuild it after a change,
run `mobile\build-apk.bat`. Keep a backup of the `mobile\keystore` folder: every update to the app
must be signed with that same key, or phones refuse to install it.

Phones reach the server over the office Wi-Fi (`http://<the machine's IP>:3001`), or over HTTPS
when a reverse proxy is in front.

---

## Moving existing data instead of starting fresh

A fresh installation loads only the demonstration stores, sites, vendors and purchase orders. To
carry across real receipts, issues and evidence, copy the database as well.

On the machine that has the data:

```sql
BACKUP DATABASE Slims TO DISK = 'C:\SLIMS\Slims.bak' WITH INIT, COMPRESSION;
```

On the new machine, before the first start:

```sql
RESTORE DATABASE Slims FROM DISK = 'C:\SLIMS\Slims.bak'
  WITH MOVE 'Slims' TO 'C:\SLIMS\data\Slims.mdf',
       MOVE 'Slims_log' TO 'C:\SLIMS\data\Slims_log.ldf';
```

Also copy the **`uploads`** folder. The before/after installation photographs live there, not in
the database. Without it, the evidence records point at missing files. QR labels need no copying:
they are drawn from the database whenever they are printed.

---

## Going to a proper server later

Nothing has to be rebuilt. Copy the same `SLIMS-Setup` folder to the VM. Only three things change:

1. Start it with `3-install-as-service-RUN-AS-ADMIN.bat` instead of the batch file.
2. Put **IIS** or **Nginx** in front for HTTPS, with a certificate for the real host name. In IIS,
   install the URL Rewrite and Application Request Routing modules, create a site on port 443, and
   add a reverse-proxy rule to `http://127.0.0.1:3001/`. `frontend/deploy/web.config` and
   `nginx-slims.conf` are ready-made. The SLIMS Field app works over plain HTTP on the office
   network, but HTTPS protects the evidence on its way from the field.
3. In `api\appsettings.Production.json`, set `"SecureCookies": true` so the session cookie only
   travels over HTTPS. Then restart the `SlimsApi` service.

Schedule a nightly backup of the `Slims` database and the `uploads` folder.

For a separate public portal and API host, use the zips from `make-release-zips.py`. They carry
their own README.

---

## If something does not start

| Symptom | Where to look |
|---|---|
| "SQL Server Express could not be started" | Install SQL Server Express, or check the `MSSQL$SQLEXPRESS` service |
| API does not start | `api.log` in the package folder. For the service, Windows Event Viewer ▸ Application |
| Web app does not start | `web.log` in the package folder |
| Colleagues cannot open it | The firewall script was not run, the network is marked Public, or the machine's IP changed |
| Phone cannot capture location or camera | The browser blocks GPS and camera on plain `http://`. Use HTTPS (see above) or allow the site in the browser settings |
| "Invalid user ID or password" | Passwords are case-sensitive. Five wrong tries lock the account for 15 minutes |
| Everyone was signed out | `Jwt:Key` in `api\appsettings.Production.json` changed. This is expected once after editing it |

The connection string, ports, upload folder and token settings are all in
`api\appsettings.Production.json`. `START-SLIMS.bat` writes that file on the first start. The
database server is written as `.\SQLEXPRESS`, meaning "SQL Express on this machine", so it works on
any PC without editing.
