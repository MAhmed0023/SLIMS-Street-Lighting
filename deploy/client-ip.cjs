// Loaded with `node -r client-ip.cjs server.js` by the LAN launchers (START-SLIMS.bat, run-web.cmd,
// start_slims.pyw). Not needed on a server behind IIS or Nginx, though harmless there.
//
// Why: the audit trail must record each user's machine (FR-SEC-005). The portal trusts only
// X-Real-IP and drops any X-Forwarded-For a browser sends, which is right behind a reverse proxy.
// On the office LAN nothing sits in front, so no X-Real-IP arrives and every action was recorded
// as 127.0.0.1. This stamps X-Real-IP with the address of the connection itself.
//
// A connection from another machine always gets its own socket address, overwriting anything the
// browser sent, so the address cannot be forged. A loopback connection is either a person on this
// PC or a local IIS / Nginx, whose own X-Real-IP is then left as it is.
"use strict";
const http = require("http");

const LOOPBACK = new Set(["127.0.0.1", "::1"]);
const emit = http.Server.prototype.emit;

http.Server.prototype.emit = function (event, req, ...rest) {
  if (event === "request" && req && req.socket) {
    const addr = String(req.socket.remoteAddress || "").replace(/^::ffff:/i, "");
    if (addr && !LOOPBACK.has(addr)) {
      req.headers["x-real-ip"] = addr;
      delete req.headers["forwarded"];
    }
  }
  return emit.call(this, event, req, ...rest);
};
