using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slims.Domain.Config;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Infrastructure.Persistence;

/// <summary>First-start data: roles, parameters, default accounts and a small demonstration data set
/// (two stores, seven sites, two vendors, two purchase orders with their QR series). Runs only on an empty
/// database; never overwrites live data.</summary>
public static class Seeder
{
    public static async Task RunAsync(AppDbContext db, ILogger log, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        var cfg = DefaultConfig.Create();
        if (!await db.Roles.AnyAsync(ct)) { db.Roles.AddRange(DefaultConfig.DefaultRoles()); await db.SaveChangesAsync(ct); }
        if (!await db.AppSettings.AnyAsync(x => x.Key == "config", ct)) { db.AppSettings.Add(new AppSetting { Key = "config", Json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }); await db.SaveChangesAsync(ct); }

        if (!await db.Users.AnyAsync(ct))
        {
            User U(string id, string name, string designation, string role, string? zone = null, string stores = "") => new() { Id = id, Name = name, Designation = designation, RoleKey = role, Zone = zone, StoreIds = stores, Active = true };
            db.Users.AddRange(
                U("sysadmin", "System Administrator", "IT Administrator", "sysadmin"),
                U("pd", "Project Director's Office", "Deputy Director (MIS)", "pdoffice"),
                U("sic-cen", "Store In-Charge, Central Store", "Controller of Stores", "storeincharge"),
                U("sk-cen", "Store Keeper, Central Store", "Store Keeper", "storekeeper"),
                U("sup-central", "Lighting Supervisor, Central Zone", "Lighting Supervisor", "supervisor", "Central"),
                U("sup-north", "Lighting Supervisor, North Zone", "Lighting Supervisor", "supervisor", "North"),
                U("sup-south", "Lighting Supervisor, South Zone", "Lighting Supervisor", "supervisor", "South"),
                U("off-central", "Lighting Officer, Central Zone", "Lighting Officer", "officer", "Central"),
                U("off-north", "Lighting Officer, North Zone", "Lighting Officer", "officer", "North"),
                U("off-south", "Lighting Officer, South Zone", "Lighting Officer", "officer", "South"),
                U("proc", "Procurement & Contracts", "Manager Procurement", "procurement"),
                U("audit", "Internal Audit", "Audit Officer", "audit"));
            await db.SaveChangesAsync(ct);
            log.LogInformation("Provisioned default accounts (no passwords; first sysadmin sign-in sets the administrator password)");
        }

        await EnsureAsync(db, log, ct);
        if (await db.Stores.AnyAsync(ct)) return;

        // ---- stores & bin locations -------------------------------------------------------------
        Store S(string code, string name, string type, string zone, double lat, double lng, string? inCharge, params (string sec, string rack, string bin, string kind)[] locs)
        {
            var s = new Store { Code = code, Name = name, Type = type, Zone = zone, Lat = lat, Lng = lng, InChargeUserId = inCharge, Address = zone + " zone" };
            foreach (var (sec, rack, bin, kind) in locs) s.Locations.Add(new StoreLocation { Section = sec, Rack = rack, Bin = bin, Kind = kind, FullCode = $"{code}-{sec}-{rack}-{bin}" });
            return s;
        }
        var cen = S("CEN01", "Central Store, Lahore", "Central", "Central", 31.5497, 74.3436, "sic-cen",
            ("LUM", "R01", "B01", "Stock"), ("LUM", "R01", "B02", "Stock"), ("LUM", "R02", "B01", "Stock"), ("POL", "Y01", "S01", "Stock"), ("BRK", "R03", "B01", "Stock"), ("CBL", "R04", "B01", "Stock"), ("ACC", "R05", "B01", "Stock"), ("QTN", "Q1", "B1", "Quarantine"), ("SCR", "S1", "B1", "Scrap"));
        var zn = S("ZNN01", "Zonal Store, North (Shahdara)", "Zonal", "North", 31.6180, 74.2950, null,
            ("LUM", "R01", "B01", "Stock"), ("ACC", "R02", "B01", "Stock"), ("QTN", "Q1", "B1", "Quarantine"), ("SCR", "S1", "B1", "Scrap"));
        db.Stores.AddRange(cen, zn);

        // ---- sites (geo-fenced) ---------------------------------------------------------------------
        Site Si(string code, string name, string zone, string division, string type, double lat, double lng, int fence, string sup, string off)
            => new() { Code = code, Name = name, Zone = zone, Division = division, Type = type, Lat = lat, Lng = lng, GeofenceM = fence, SupervisorUserId = sup, OfficerUserId = off, Status = "Active" };
        db.Sites.AddRange(
            Si("SITE-C-MALL-0001", "Mall Road — GPO to Regal Chowk", "Central", "Lahore City", "Road stretch", 31.5610, 74.3170, 250, "sup-central", "off-central"),
            Si("SITE-C-LIBR-0002", "Liberty Roundabout", "Central", "Gulberg", "Junction", 31.5100, 74.3440, 60, "sup-central", "off-central"),
            Si("SITE-C-JAIL-0003", "Jail Road — Shadman signal", "Central", "Lahore City", "Junction", 31.5390, 74.3300, 80, "sup-central", "off-central"),
            Si("SITE-N-GTRD-0004", "GT Road — Shahdara Morr", "North", "Shahdara", "Junction", 31.6180, 74.2950, 100, "sup-north", "off-north"),
            Si("SITE-N-RAVI-0005", "Ravi Road Junction", "North", "Ravi Town", "Junction", 31.5960, 74.3080, 80, "sup-north", "off-north"),
            Si("SITE-S-KALM-0006", "Ferozepur Road — Kalma Chowk", "South", "Gulberg", "Junction", 31.5040, 74.3260, 120, "sup-south", "off-south"),
            Si("SITE-S-MTLR-0007", "Model Town Link Road", "South", "Model Town", "Road stretch", 31.4780, 74.3200, 300, "sup-south", "off-south"));

        // ---- vendors & purchase orders ----------------------------------------------------------------
        var v1 = new Vendor { Code = "V0231", Name = "Pak Lighting Industries (Pvt) Ltd", Contact = "Sales Manager", Phone = "042-3571 2200", Email = "sales@paklighting.example" };
        var v2 = new Vendor { Code = "V0198", Name = "Metro Poles & Fabricators", Contact = "Works Manager", Phone = "042-3680 1144", Email = "orders@metropoles.example" };
        db.Vendors.AddRange(v1, v2);
        PurchaseOrderLine L(int n, string cat, string sub, string spec, string desc, string uom, decimal qty, decimal rate) => new() { LineNo = n, Category = cat, SubCategory = sub, Spec = spec, Description = desc, Uom = uom, Qty = qty, Rate = rate };
        var po1 = new PurchaseOrder { Number = "PO-2611", Vendor = v1, Date = new DateOnly(2026, 7, 14), DeliveryDue = new DateOnly(2026, 9, 30), WarrantyMonths = 24, CreatedBy = "seed", Remarks = "LED luminaires and controllers — Phase-I",
            Lines = { L(1, "LUM", "LED", "090W", "LED street light luminaire 90W, IP66, 130 lm/W, 5700K", "Nos", 120, 18500), L(2, "LUM", "LED", "150W", "LED street light luminaire 150W, IP66, 130 lm/W, 5700K", "Nos", 60, 26500), L(3, "ACC", "PHC", "10A", "Photocell controller 10A, NEMA socket", "Nos", 180, 1200) } };
        var po2 = new PurchaseOrder { Number = "PO-2634", Vendor = v2, Date = new DateOnly(2026, 8, 2), DeliveryDue = new DateOnly(2026, 10, 15), WarrantyMonths = 36, CreatedBy = "seed", Remarks = "Poles, brackets and cable — Phase-I",
            Lines = { L(1, "POL", "OCT", "9M", "Octagonal galvanised steel pole 9 m, 3 mm, with base plate", "Nos", 40, 48000), L(2, "BRK", "DBL", "1.5M", "Double-arm bracket 1.5 m, galvanised", "Nos", 40, 6500), L(3, "CBL", "CU", "4MM2", "Copper cable 2-core 4 mm², PVC/PVC, per metre", "Metre", 2000, 320) } };
        db.PurchaseOrders.AddRange(po1, po2);
        await db.SaveChangesAsync(ct);

        // ---- articles + QR series (mirrors MasterService.CreateArticleAsync) -----------------------
        var now = DateTime.UtcNow;
        var locs = cen.Locations.ToDictionary(l => l.FullCode);
        (Article a, List<Item> items) Make(PurchaseOrder po, PurchaseOrderLine line, bool serialised, decimal pack, decimal reorder, int? life)
        {
            var count = serialised ? (int)Math.Ceiling(line.Qty) : (int)Math.Ceiling(line.Qty / pack);
            var a = new Article { ItemCode = Codes.ItemCode(line.Category, line.SubCategory, line.Spec, po.Number, line.LineNo), Category = line.Category, SubCategory = line.SubCategory, Spec = line.Spec, Description = line.Description, Uom = line.Uom,
                Serialised = serialised, PackSize = pack, PurchaseOrderId = po.Id, PurchaseOrderLineId = line.Id, VendorId = po.VendorId, Rate = line.Rate, WarrantyMonths = po.WarrantyMonths, ExpectedLifeMonths = life, ReorderLevel = reorder, ItemsGenerated = count, CreatedBy = "seed" };
            var items = new List<Item>(); var remaining = line.Qty;
            for (var s = 1; s <= count; s++) { var q = serialised ? 1 : Math.Min(pack, remaining); remaining -= q; items.Add(new Item { Id = Guid.NewGuid(), Article = a, Serial = s, Qty = q, QrCode = Codes.QrPayload(a.ItemCode, s), Status = ItemStatus.Created, UpdatedBy = "seed" }); }
            db.Articles.Add(a); db.Items.AddRange(items);
            return (a, items);
        }
        void Receive(List<Item> items, StoreLocation loc, Grn grn, int take, int daysAgo)
        {
            foreach (var i in items.Take(take))
            {
                i.Status = ItemStatus.Received; i.StoreLocation = loc; i.Grn = grn; i.ReceivedAt = now.AddDays(-daysAgo); i.UpdatedAt = now.AddDays(-daysAgo);
                grn.ItemCount += 1; grn.Qty += i.Qty; grn.Value += i.Qty * i.Article!.Rate;
                // the same audit row a real receipt writes, so a seeded label traces back to its GRN too
                db.AuditLogs.Add(new AuditLog
                {
                    At = now.AddDays(-daysAgo), Kind = "stock", Action = "received", EntityType = "item", EntityId = i.Id.ToString(),
                    ActorId = grn.PostedById, ActorName = grn.PostedBy, Role = "storekeeper",
                    NextJson = JsonSerializer.Serialize(new { Grn = grn.Number, Location = loc.FullCode, Po = grn.PurchaseOrder!.Number, i.Qty }),
                    Reason = grn.ChallanNo is null ? null : "Challan " + grn.ChallanNo,
                });
            }
        }
        var (a90, i90) = Make(po1, po1.Lines[0], true, 1, 20, 60);
        var (a150, i150) = Make(po1, po1.Lines[1], true, 1, 10, 60);
        var (aphc, iphc) = Make(po1, po1.Lines[2], false, 10, 30, 36);
        var (apol, ipol) = Make(po2, po2.Lines[0], true, 1, 5, 240);
        Make(po2, po2.Lines[1], true, 1, 5, 240);
        Make(po2, po2.Lines[2], false, 100, 300, 120);
        var fy = Codes.FY(now);
        var g1 = new Grn { Number = $"GRN/CEN01/{fy}/00001", PurchaseOrder = po1, Store = cen, ChallanNo = "DC-4471", InvoiceNo = "INV-2026-0912", InvoiceDate = new DateOnly(2026, 8, 20), PostedById = "sk-cen", PostedBy = "Store Keeper, Central Store", PostedAt = now.AddDays(-25), Remarks = "Seed: first consignment against PO-2611" };
        var g2 = new Grn { Number = $"GRN/CEN01/{fy}/00002", PurchaseOrder = po2, Store = cen, ChallanNo = "MP-0913", InvoiceNo = "MPF-118", InvoiceDate = new DateOnly(2026, 9, 1), PostedById = "sk-cen", PostedBy = "Store Keeper, Central Store", PostedAt = now.AddDays(-12), Remarks = "Seed: poles received; brackets and cable still awaited" };
        db.Grns.AddRange(g1, g2);
        Receive(i90, locs["CEN01-LUM-R01-B01"], g1, 100, 25);   // 20 luminaires of 120 still to arrive → GRN demo
        Receive(i150, locs["CEN01-LUM-R01-B02"], g1, 60, 25);
        Receive(iphc, locs["CEN01-ACC-R05-B01"], g1, 18, 25);
        Receive(ipol, locs["CEN01-POL-Y01-S01"], g2, 40, 12);
        db.DocSequences.AddRange(new DocSequence { Key = $"GRN/CEN01/{fy}", Next = 3 });
        await db.SaveChangesAsync(ct);
        db.AuditLogs.Add(new AuditLog { Kind = "config", Action = "seed", ActorId = "seed", ActorName = "First-start seed", Reason = "Roles, parameters, default accounts, demonstration stores, sites, vendors, purchase orders, QR series and two opening GRNs" });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded demonstration data: 2 stores, 7 sites, 2 vendors, 2 purchase orders, {n} QR labels", await db.Items.CountAsync(ct));
        await EnsureAsync(db, log, ct);
    }

    /// <summary>Brings an existing database up to the current rules on every start. Idempotent.</summary>
    static async Task EnsureAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        // the audit permission (DF-01) for the roles that read the whole trail
        foreach (var r in await db.Roles.Where(r => r.Key == "sysadmin" || r.Key == "pdoffice" || r.Key == "audit").ToListAsync(ct))
            if (!r.Has(Permissions.Audit)) r.Perms = string.Join(',', r.PermList.Append(Permissions.Audit));
        // every store has a Receiving area (FR-INV-010)
        foreach (var s in await db.Stores.Include(s => s.Locations).ToListAsync(ct))
            if (!s.Locations.Any(l => l.Kind == "Receiving")) s.Locations.Add(new StoreLocation { Section = "RCV", Rack = "R0", Bin = "B0", Kind = "Receiving", FullCode = $"{s.Code}-RCV-R0-B0" });
        await db.SaveChangesAsync(ct);
        // the quantity ledger starts from what the stores hold the first time it runs (FR-INV-016)
        if (!await db.StockMovements.AnyAsync(ct))
        {
            var inStore = new[] { ItemStatus.Received, ItemStatus.Reserved, ItemStatus.Returned, ItemStatus.UnderRepair, ItemStatus.WarrantyClaim, ItemStatus.Scrap };
            var items = await db.Items.Include(i => i.StoreLocation).Where(i => i.StoreLocationId != null && inStore.Contains(i.Status)).ToListAsync(ct);
            foreach (var i in items)
                db.StockMovements.Add(new StockMovement { Type = "Opening", ItemId = i.Id, ArticleId = i.ArticleId, StoreId = i.StoreLocation!.StoreId, ToLocationId = i.StoreLocationId, Qty = i.Qty, RefType = "opening", RefNo = "ledger start", UserId = "system", At = i.ReceivedAt ?? i.ReturnedAt ?? DateTime.UtcNow });
            if (items.Count > 0) { await db.SaveChangesAsync(ct); log.LogInformation("Stock ledger opened with {n} item balances", items.Count); }
        }
    }
}
