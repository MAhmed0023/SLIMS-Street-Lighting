PERA Workflow - Task, Document Movement, Approval & Signature System
====================================================================

1. Copy this whole folder to the PC, e.g. C:\PERA   (Windows 10/11 or Server 2019+, 64-bit)
2. Double-click START-PERA.bat
3. The browser opens  http://localhost:3002
   Other PCs on the network:  http://<this PC's IP>:3002  (allow ports 3002 in Windows Firewall)

First sign-in
   User ID  sysadmin  - the password you type becomes the administrator password
   (at least 8 characters, letters and digits). Then open Settings > Users & Access
   and issue a temporary password to each officer (adg, dir-ops, dd-ops, so-ops,
   legal, rnd, audit, wfadmin ...). They choose their own at first sign-in.

Database
   If SQL Server Express (instance SQLEXPRESS) is installed, PERA creates the
   database "Pera" there. If not, it uses a demonstration database file in
   data\pera-demo.db - fine for trying it out, not for real records.

Nothing else needs installing: the package carries its own .NET runtime and Node.js.
Stop it with STOP-PERA.bat.
