# SLIMS standalone demo

`docs/SLIMS_Demo.html` is the whole of SLIMS in one file: the real portal pages (`../frontend`) and the real
SLIMS Field app (`../mobile`, in the phone frame), running on a snapshot of a demo database. It needs no server
and no install; double-click it. Internet is used only for the map tiles and the fonts.

- Sign in by picking any user: each role sees its own menus and permissions.
- The main workflows run in the page (requisition → approval → issue by quantity, goods receipt by quantity,
  phone set-up, installation with photographs and geo-fence check, verification, returns, adjustments, transfers,
  counts, amendments, master data). Changes last until the tab is closed; **Reset** starts again.
- E-mail/SMS, bulk site upload and the APK download need the live server.

## Rebuilding it

1. Start an API on a demo database: `C:\Users\User\slims-demo\run-api.sh` (port 5192, database `SlimsDemo`).
   For a fresh database, sign in once as `sysadmin` with the demo password, then run
   `python C:\Users\User\slims-demo\populate.py` to create the transactions and photographs.
2. `npm run crawl` — snapshots every screen into `src/data/snapshot.json`.
3. `npm run build` — writes `dist/index.html`; copy it to `../docs/SLIMS_Demo.html`.

`src/mock/server.ts` answers the API in the browser; `src/mock/devices.ts` supplies the demo camera and GPS;
`vite.config.ts` swaps Next.js for two small shims (`src/shims`).
