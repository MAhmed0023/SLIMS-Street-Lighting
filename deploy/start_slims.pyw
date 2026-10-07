# -*- coding: utf-8 -*-
r"""SLIMS (Street Lighting Store, Issuance & Installation Management) — office launcher / keep-alive
for THIS machine. Other machines use the SLIMS-Setup package and START-SLIMS.bat instead.

    pythonw start_slims.pyw            start both parts if they are not answering, then open the app
    pythonw start_slims.pyw --watch    stay in the background (Windows Startup): check every 20 s and restart whatever is down
    pythonw start_slims.pyw --stop     stop both parts

Two processes:
  * API       deploy\api\Slims.Api.exe                   http://127.0.0.1:5091   (localhost only — the web app proxies to it)
  * Web app   frontend\.next\standalone\server.js         http://0.0.0.0:3001     (this is what the office opens)

Colleagues open   http://<this machine's IP>:3001   — see the address printed in slims.log at start-up.
PIMRS runs beside it on 3000/5090; nothing here touches those ports.
Log: slims.log next to this file.
"""
import ctypes
import os
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
API_DIR = os.path.join(HERE, "api")
API_EXE = os.path.join(API_DIR, "Slims.Api.exe")
WEB_DIR = os.path.join(ROOT, "frontend", ".next", "standalone")
API_URL = "http://127.0.0.1:5091/api/health"
API_PORT = 5091
WEB_PORT = 3001
LOG = os.path.join(HERE, "slims.log")
CHECK_EVERY = 20
NO_WINDOW = 0x00000008 | 0x00000200 | 0x08000000  # DETACHED_PROCESS | NEW_PROCESS_GROUP | NO_WINDOW


def log(msg):
    try:
        if os.path.exists(LOG) and os.path.getsize(LOG) > 300_000:
            os.replace(LOG, LOG + ".old")
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(time.strftime("%Y-%m-%d %H:%M:%S ") + msg + "\n")
    except OSError:
        pass


def lan_ip():
    """The address colleagues type in their browser."""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except OSError:
        return "127.0.0.1"


def up(url, timeout=3):
    try:
        with urllib.request.urlopen(url, timeout=timeout) as r:
            return r.status == 200
    except Exception:
        return False


def api_up():
    return up(API_URL)


def web_up():
    return up("http://127.0.0.1:%d/login" % WEB_PORT, timeout=5)


def free_port(port):
    """Something half-dead may still hold the port (a crashed server that never let go)."""
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Get-NetTCPConnection -LocalPort %d -State Listen -ErrorAction SilentlyContinue | "
                    "ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }" % port],
                   capture_output=True, creationflags=0x08000000)


def start_api():
    if not os.path.exists(API_EXE):
        log("no API build at %s — run publish-local.bat" % API_EXE)
        return False
    free_port(API_PORT)
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Production")
    with open(os.path.join(HERE, "api.log"), "a", encoding="utf-8") as out:
        subprocess.Popen([API_EXE], cwd=API_DIR, env=env, creationflags=NO_WINDOW, close_fds=True,
                         stdin=subprocess.DEVNULL, stdout=out, stderr=out)
    for _ in range(90):
        time.sleep(0.5)
        if api_up():
            log("api started")
            return True
    log("api did not answer — see api.log")
    return False


def node_exe():
    return shutil.which("node") or r"C:\Program Files\nodejs\node.exe"


def start_web():
    server = os.path.join(WEB_DIR, "server.js")
    if not os.path.exists(server):
        log("no web build at %s — run publish-local.bat" % server)
        return False
    free_port(WEB_PORT)
    # The standalone server takes its address from the environment. HOSTNAME=0.0.0.0 is what makes
    # it reachable from the office network rather than only from this PC.
    env = dict(os.environ, HOSTNAME="0.0.0.0", PORT=str(WEB_PORT), NODE_ENV="production")
    with open(os.path.join(HERE, "web.log"), "a", encoding="utf-8") as out:
        # client-ip.cjs records each user's own machine in the audit trail — see that file.
        subprocess.Popen([node_exe(), "-r", os.path.join(HERE, "client-ip.cjs"), "server.js"], cwd=WEB_DIR, env=env,
                         creationflags=NO_WINDOW, close_fds=True, stdin=subprocess.DEVNULL, stdout=out, stderr=out)
    for _ in range(90):
        time.sleep(0.5)
        if web_up():
            log("web app started on http://%s:%d" % (lan_ip(), WEB_PORT))
            return True
    log("web app did not answer — see web.log")
    return False


def stop():
    subprocess.run(["taskkill", "/F", "/IM", "Slims.Api.exe"], capture_output=True)
    free_port(API_PORT)
    free_port(WEB_PORT)
    log("stopped")


def browser_exe():
    for base in (os.environ.get("ProgramFiles", r"C:\Program Files"),
                 os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                 os.environ.get("LOCALAPPDATA", "")):
        for rel in (r"Microsoft\Edge\Application\msedge.exe", r"Google\Chrome\Application\chrome.exe"):
            p = os.path.join(base, rel)
            if os.path.exists(p):
                return p
    return None


def open_app():
    url = "http://localhost:%d" % WEB_PORT
    exe = browser_exe()
    if exe:
        try:
            subprocess.Popen([exe, "--app=" + url, "--window-size=1440,920"], close_fds=True)
            return
        except OSError:
            pass
    webbrowser.open(url)


def ensure():
    ok = True
    if not api_up():
        ok = start_api() and ok
    if not web_up():
        ok = start_web() and ok
    return ok


def main():
    if "--stop" in sys.argv:
        stop()
        return
    if "--watch" in sys.argv:
        ctypes.windll.kernel32.CreateMutexW(None, False, "SLIMS_Watch")
        if ctypes.windll.kernel32.GetLastError() == 183:  # already running
            return
        log("watch started — office address http://%s:%d" % (lan_ip(), WEB_PORT))
        while True:
            if not api_up() or not web_up():
                log("service down — restarting")
                ensure()
            time.sleep(CHECK_EVERY)
    if not ensure():
        ctypes.windll.user32.MessageBoxW(None, "SLIMS could not start.\nSee slims.log, api.log and web.log in the deploy folder.", "SLIMS", 0x10)
        return
    log("ready — office address http://%s:%d" % (lan_ip(), WEB_PORT))
    if "--no-browser" not in sys.argv:
        open_app()


if __name__ == "__main__":
    main()
