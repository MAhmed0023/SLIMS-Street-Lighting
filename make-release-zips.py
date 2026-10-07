"""Builds the two hand-over packages for the public pera.gop.pk servers.

    SLIMS-frontend-<date>.zip   the portal   -> PORTAL below
    SLIMS-backend-<date>.zip    the API      -> API below

Each zip carries the source, a ready-to-run build, the production configuration for that host
and a README. Nothing here touches the office LAN install, which keeps pointing at 127.0.0.1.

    python make-release-zips.py            build everything, then zip
    python make-release-zips.py --no-build use whatever is already built

The host names below are placeholders until IT confirms the real ones - change the two lines and
rebuild. The portal build bakes the API address in, so it has to be right before handing over.
"""
import io
import os
import shutil
import socket
import subprocess
import sys
import zipfile
from datetime import date

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, "release")
STAMP = date.today().strftime("%Y-%m-%d")

PORTAL = "https://streetlight.pera.gop.pk"
API = "https://streetlightapi.pera.gop.pk"

SKIP_DIRS = {"node_modules", ".next", "bin", "obj", ".git", ".vs", "release", "__pycache__", "uploads"}
SKIP_FILES = {".env.local", "npm-debug.log", "Thumbs.db", "appsettings.Development.json"}


def run(cmd, cwd):
    print("  $", " ".join(cmd))
    r = subprocess.run(cmd, cwd=cwd, shell=True)
    if r.returncode != 0:
        sys.exit(f"failed: {' '.join(cmd)}")


def copy_tree(src, dst, skip_dirs=SKIP_DIRS):
    for base, dirs, names in os.walk(src):
        dirs[:] = [d for d in dirs if d not in skip_dirs]
        rel = os.path.relpath(base, src)
        target = dst if rel == "." else os.path.join(dst, rel)
        os.makedirs(target, exist_ok=True)
        for n in names:
            if n in SKIP_FILES:
                continue
            shutil.copy2(os.path.join(base, n), os.path.join(target, n))


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    io.open(path, "w", encoding="utf-8", newline="\n").write(text)


def zip_dir(folder, zip_path):
    total = 0
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for base, _dirs, names in os.walk(folder):
            for n in names:
                full = os.path.join(base, n)
                z.write(full, os.path.relpath(full, folder))
                total += 1
    mb = os.path.getsize(zip_path) / 1024 / 1024
    print(f"  -> {os.path.basename(zip_path)}  {total} files, {mb:.1f} MB")


# ---------------------------------------------------------------- frontend ----
FRONT_README = f"""# SLIMS portal — {PORTAL}

Street Lighting Store, Issuance & Installation Management System — web portal (Next.js 16).
Government Street Lighting Project.

## What is in here

    deploy/   the deployment script and the reverse-proxy configuration
    run/      a ready-to-run build - no npm install needed
    src/      the full source
    README.md

## Deploy it

The portal needs Node.js 20 or newer on the server. From an **elevated** PowerShell:

    cd deploy
    .\\Deploy-Portal.ps1 -DryRun     # prints every step, changes nothing
    .\\Deploy-Portal.ps1             # does it

That copies the build to `C:\\SLIMS\\portal`, registers it to start with Windows, starts it and waits
until it answers on `http://127.0.0.1:3001`. The previous build is kept, so
`.\\Deploy-Portal.ps1 -Rollback` puts it back.

`Start-Portal.cmd` and `Stop-Portal.cmd` start and stop it by hand. Stop the portal before
replacing any file: Windows keeps the folder locked while Node is running.

Useful switches: `-InstallDir`, `-Port`, `-BindAddress`, `-TaskName`, `-PublicUrl`.
`Get-Help .\\Deploy-Portal.ps1 -Full` has the rest.

### By hand, without the script

    cd run
    set NODE_ENV=production
    set HOSTNAME=127.0.0.1
    set PORT=3001
    node server.js

### The reverse proxy

`{PORTAL}` terminates HTTPS and forwards to `http://127.0.0.1:3001`.

  - IIS: copy `deploy/web.config` into the site root. It needs URL Rewrite and Application
    Request Routing, with the proxy enabled and the server variables `HTTP_X_REAL_IP` and
    `HTTP_X_FORWARDED_PROTO` allowed. The file says how.
  - Nginx: use `deploy/nginx-slims.conf`.

Either way the proxy must set **X-Real-IP**. The audit trail records who changed what from which
machine, and the portal deliberately ignores any `X-Forwarded-For` a browser sends so that nobody
can write a false address into it. `X-Real-IP` from your own proxy is the only source it trusts.
Both configurations raise the upload limit to 80 MB; the API accepts 64 MB of evidence per request.

HTTPS is required, not just recommended: phone browsers only give the camera and GPS to a secure
page, and Lighting Officers capture the geo-stamped before/after installation photographs that way.

## The API address

The browser never calls the API host directly. Next forwards `/api/*` to the API server side,
which keeps the refresh cookie first-party on the portal domain and avoids CORS entirely.

`run/` was built against `{API}` and that address is **fixed inside the build**.
Next resolves the rewrite when it compiles, not when it starts, so setting `API_URL` on the
server changes nothing. To point the portal at a different API, edit `API_URL` in
`src/.env.production` and rebuild — see below.

## Rebuild from source

    cd src
    npm ci
    npm run build          # reads API_URL from .env.production and bakes it into the build

`npm run build` also copies `.next/static` and `public/` into `.next/standalone`, which is what
`run/` contains. The build fails with EBUSY if the portal is running — stop it first.

## Checklist before going live

  - HTTPS certificate on {PORTAL}
  - the reverse proxy passes the real client address in `X-Real-IP` or `Forwarded`
  - the API answers from the portal server: `curl {API}/api/health` returns `"ok":true`
  - port 3001 is not reachable from outside; only the proxy should be able to reach it
  - a phone on mobile data can open the portal, sign in as a Lighting Officer and is asked for
    camera and location permission on the evidence screen
"""

# ---------------------------------------------------------------- backend -----
BACK_README = f"""# SLIMS API — {API}

Street Lighting Store, Issuance & Installation Management System — application server
(.NET 9, Clean Architecture, Entity Framework Core, SQL Server).

## What is in here

    src/                          the full source (Domain, Application, Infrastructure, Api)
    run/                          a self-contained build — no .NET runtime needed on the server
    appsettings.Production.json   the configuration for {API}
    README.md

## Before the first start

1. **Database.** Create an empty database called `Slims` and point the connection string at it.
   The API applies its own migrations on start, so no script has to be run by hand. On the very
   first start it also seeds the user accounts (without passwords) and the demonstration master
   data; the first `sysadmin` sign-in sets the administrator password.

2. **JWT signing key.** `appsettings.Production.json` ships with an empty `Jwt:Key`. Put a random
   secret of at least 32 characters there, keep it out of source control, and never reuse the one
   from another environment. Changing it signs every user out once. An empty key is not safe: the
   API then derives one from the machine name.

3. **Uploads folder.** `App:UploadsDir` must exist and be writable by the service account or
   application pool identity. The geo-stamped before/after installation photographs live there;
   back it up with the database.

## Run it

Copy `run/` and `appsettings.Production.json` onto the server (the settings file goes inside `run/`),
then either

  - run it as a Windows service — the API supports this natively:

        sc create SlimsApi binPath= "C:\\SLIMS\\api\\Slims.Api.exe" start= auto obj= "NT AUTHORITY\\NetworkService"
        sc failure SlimsApi reset= 86400 actions= restart/5000/restart/10000/restart/30000
        sc start SlimsApi

    The service account then needs a SQL login with rights on the `Slims` database, and modify
    rights on the uploads folder.

  - or host it under IIS with the ASP.NET Core Module.

Kestrel listens on `http://127.0.0.1:5091`. Put IIS or Nginx in front so `{API}`
terminates HTTPS and forwards to it. `GET /api/health` answers without signing in.

## Configuration that matters

    App:AllowedOrigins   ["{PORTAL}"]
                         Only needed if a browser ever calls this API directly. In the normal
                         setup the portal proxies /api server side and no CORS request is made.

    App:SecureCookies    true — the refresh cookie is then only sent over HTTPS. Leave it true.
                         It must be false only on a plain-HTTP install.

    App:UploadsDir       where evidence photographs are written. A relative path is taken from
                         the folder that holds Slims.Api.exe.

    Jwt:AccessMinutes    60     how long an access token lasts
    Jwt:RefreshDays      7      how long a signed-in session survives without use

Operational thresholds — geo-fence radius, GPS accuracy, receipt tolerance, clock drift, SLA hours —
are not in this file. They live in the database and are changed in the portal under
**Settings ▸ Parameters**.

## Rebuild from source

    cd src
    dotnet publish src/Slims.Api -c Release -r win-x64 --self-contained true -o ../run

## Checklist before going live

  - HTTPS certificate on {API}
  - `Jwt:Key` filled in with a fresh secret
  - the connection string points at the production database
  - `App:UploadsDir` exists, is writable, and is included in the backup
  - Swagger (`/swagger`) is fine on the internal network; block it at the proxy if the API host is public
  - nightly backup of both the database and the uploads folder
"""

API_SETTINGS = """{
  "ConnectionStrings": {
    "Default": "Server=SQLSERVER;Database=Slims;User Id=slims;Password=CHANGE-ME;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://127.0.0.1:5091"
      }
    }
  },
  "Jwt": {
    "Issuer": "Slims",
    "Audience": "Slims",
    "Key": "",
    "AccessMinutes": 60,
    "RefreshDays": 7
  },
  "App": {
    "UploadsDir": "C:\\\\SLIMS\\\\uploads",
    "AllowedOrigins": [
      "%PORTAL%"
    ],
    "SecureCookies": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Error"
    }
  },
  "AllowedHosts": "*"
}
""".replace("%PORTAL%", PORTAL)

ENV_PRODUCTION = f"""# The portal forwards /api to the API host, so the browser only ever talks to {PORTAL}.
# Change this one line if the API moves, then rebuild.
API_URL={API}
"""


# ---------------------------------------------------------------- live copy ---
def live_running():
    """The live SLIMS on this PC runs from frontend/.next/standalone, which a build replaces."""
    try:
        with socket.create_connection(("127.0.0.1", 3001), timeout=1):
            return True
    except OSError:
        return False


def live(action):
    launcher = os.path.join(ROOT, "deploy", "start_slims.pyw")
    if not os.path.exists(launcher):
        return
    if action == "stop":
        subprocess.run(["pythonw", launcher, "--stop"])
    else:
        subprocess.Popen(["pythonw", launcher, "--no-browser"], close_fds=True)


def build_frontend(stage, do_build):
    src = os.path.join(ROOT, "frontend")
    if do_build:
        print("frontend: building")
        env = dict(os.environ, API_URL=API, NODE_ENV="production")
        r = subprocess.run("npm run build", cwd=src, shell=True, env=env)
        if r.returncode != 0:
            sys.exit("frontend build failed")
    print("frontend: staging")
    copy_tree(src, os.path.join(stage, "src"))
    write(os.path.join(stage, "src", ".env.production"), ENV_PRODUCTION)
    standalone = os.path.join(src, ".next", "standalone")
    if not os.path.isdir(standalone):
        sys.exit("no .next/standalone — run without --no-build")
    copy_tree(standalone, os.path.join(stage, "run"), skip_dirs={".git"})
    copy_tree(os.path.join(src, "deploy"), os.path.join(stage, "deploy"))
    write(os.path.join(stage, "README.md"), FRONT_README)
    if do_build:
        # Next resolves rewrites at compile time, so the tree is now pointing at the public API.
        # Build it once more with API_URL unset, which is what the office LAN install runs on.
        print("frontend: restoring the local build (127.0.0.1 API)")
        env = {k: v for k, v in os.environ.items() if k != "API_URL"}
        env["NODE_ENV"] = "production"
        r = subprocess.run("npm run build", cwd=src, shell=True, env=env)
        if r.returncode != 0:
            sys.exit("local rebuild failed — the office install is left on the public API address")


def build_backend(stage, do_build):
    src = os.path.join(ROOT, "backend")
    run_dir = os.path.join(stage, "run")
    if do_build:
        print("backend: publishing")
        run(["dotnet", "publish", "src/Slims.Api", "-c", "Release", "-r", "win-x64",
             "--self-contained", "true", "-p:PublishSingleFile=false", "-o", run_dir, "--nologo"], cwd=src)
    else:
        copy_tree(os.path.join(ROOT, "deploy", "api"), run_dir, skip_dirs={"uploads"})
    print("backend: staging")
    copy_tree(src, os.path.join(stage, "src"))
    # the published folder may carry this machine's settings — replace them with the server ones
    for junk in ("appsettings.Production.json", "appsettings.Development.json"):
        p = os.path.join(run_dir, junk)
        if os.path.exists(p):
            os.remove(p)
    shutil.rmtree(os.path.join(run_dir, "uploads"), ignore_errors=True)
    # the SLIMS Field app, offered to phones on Mobile Setup (served from run/downloads)
    published = os.path.join(ROOT, "deploy", "api", "downloads")   # build-apk.bat puts the APK and its version here
    if os.path.exists(os.path.join(published, "slims-field.apk")):
        os.makedirs(os.path.join(run_dir, "downloads"), exist_ok=True)
        for name in ("slims-field.apk", "slims-field.json"):
            if os.path.exists(os.path.join(published, name)):
                shutil.copy2(os.path.join(published, name), os.path.join(run_dir, "downloads", name))
    else:
        print("backend: no field app APK - run mobile/build-apk.bat to include it")
    write(os.path.join(stage, "appsettings.Production.json"), API_SETTINGS)
    write(os.path.join(stage, "README.md"), BACK_README)


def main():
    do_build = "--no-build" not in sys.argv
    was_live = do_build and live_running()
    if was_live:
        print("stopping the live SLIMS on this PC for the build - it restarts afterwards")
        live("stop")
    try:
        if os.path.isdir(OUT):
            shutil.rmtree(OUT)
        os.makedirs(OUT)
        for name, fn in (("frontend", build_frontend), ("backend", build_backend)):
            stage = os.path.join(OUT, f"SLIMS-{name}")
            fn(stage, do_build)
            zip_dir(stage, os.path.join(OUT, f"SLIMS-{name}-{STAMP}.zip"))
            shutil.rmtree(stage)
    finally:
        if was_live:
            print("restarting the live SLIMS on this PC")
            live("start")
    print(f"\ndone — {OUT}")


if __name__ == "__main__":
    main()
