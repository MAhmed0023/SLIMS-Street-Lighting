using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Stores and locations, sites, vendors, purchase orders, articles and the QR item series.</summary>
public class MasterService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, NumberingService numbers)
{
    // ---- stores & locations ----------------------------------------------------------------
    public async Task<List<StoreDto>> StoresAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var stores = await db.Stores.AsNoTracking().Include(s => s.Locations).OrderBy(s => s.Code).ToListAsync(ct);
        var counts = await db.Items.Where(i => i.StoreLocationId != null).GroupBy(i => i.StoreLocationId!.Value).Select(g => new { Loc = g.Key, N = g.Count(), V = g.Sum(i => i.Qty * i.Article!.Rate) }).ToDictionaryAsync(x => x.Loc, ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return stores.Where(s => AccessPolicy.StoreVisible(me, s)).Select(s =>
        {
            var locs = s.Locations.OrderBy(l => l.FullCode).Select(l => Map.Location(l, counts.TryGetValue(l.Id, out var c) ? c.N : 0)).ToList();
            var n = s.Locations.Sum(l => counts.TryGetValue(l.Id, out var c) ? c.N : 0); var v = s.Locations.Sum(l => counts.TryGetValue(l.Id, out var c) ? c.V : 0);
            return new StoreDto(s.Id, s.Code, s.Name, s.Type, s.Zone, s.Address, s.Lat, s.Lng, s.InChargeUserId, s.InChargeUserId is not null && names.TryGetValue(s.InChargeUserId, out var nm) ? nm : null, s.Active, n, v, locs);
        }).ToList();
    }

    public async Task<int> UpsertStoreAsync(int? id, StoreUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        var code = Codes.Clean(r.Code, 8);
        if (code.Length < 3) throw AppException.Bad("Store code must be at least 3 letters or digits.");
        var s = id is null ? null : await db.Stores.Include(x => x.Locations).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && s is null) throw AppException.NotFound("Store");
        if (await db.Stores.AnyAsync(x => x.Code == code && x.Id != (id ?? 0), ct)) throw AppException.Bad($"Store code {code} already exists.");
        var created = s is null;
        if (s is null) { s = new Store(); db.Stores.Add(s); }
        else if (!r.Active && s.Active && await db.Items.AnyAsync(i => i.StoreLocation!.StoreId == s.Id, ct)) throw AppException.Rule("BR-024", "A store holding stock cannot be deactivated.");
        s.Code = code; s.Name = r.Name.Trim(); s.Type = r.Type; s.Zone = r.Zone; s.Address = r.Address; s.Lat = r.Lat; s.Lng = r.Lng; s.InChargeUserId = string.IsNullOrWhiteSpace(r.InChargeUserId) ? null : r.InChargeUserId; s.Active = r.Active;
        foreach (var l in s.Locations) l.FullCode = FullCode(s, l);
        if (created)
        {
            s.Locations.Add(new StoreLocation { Section = "RCV", Rack = "R0", Bin = "B0", Kind = "Receiving" });
            s.Locations.Add(new StoreLocation { Section = "QTN", Rack = "Q1", Bin = "B1", Kind = "Quarantine" });
            s.Locations.Add(new StoreLocation { Section = "SCR", Rack = "S1", Bin = "B1", Kind = "Scrap" });
            foreach (var l in s.Locations) l.FullCode = FullCode(s, l);
        }
        audit.Log("master", created ? "store-created" : "store-updated", me, "store", s.Code, next: new { s.Code, s.Name, s.Type, s.Zone, s.Active });
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    static string FullCode(Store s, StoreLocation l) => string.Join('-', new[] { s.Code, Codes.Clean(l.Section, 6), Codes.Clean(l.Rack, 6), Codes.Clean(l.Bin, 6) }.Where(x => x != ""));

    public async Task<int> UpsertLocationAsync(int storeId, int? id, LocationUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Configure, Permissions.Receive);
        var s = await db.Stores.FirstOrDefaultAsync(x => x.Id == storeId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, s);
        var l = id is null ? null : await db.StoreLocations.FirstOrDefaultAsync(x => x.Id == id && x.StoreId == storeId, ct);
        if (id is not null && l is null) throw AppException.NotFound("Location");
        var created = l is null;
        if (l is null) { l = new StoreLocation { StoreId = storeId }; db.StoreLocations.Add(l); }
        else if (!r.Active && l.Active && await db.Items.AnyAsync(i => i.StoreLocationId == l.Id, ct)) throw AppException.Rule("BR-024", "A location holding stock cannot be deactivated.");
        if (string.IsNullOrWhiteSpace(r.Section)) throw AppException.Bad("Section is required.");
        l.Section = r.Section.Trim(); l.Rack = r.Rack.Trim(); l.Bin = r.Bin.Trim(); l.Kind = r.Kind is "Quarantine" or "Scrap" or "Receiving" ? r.Kind : "Stock"; l.Active = r.Active; l.FullCode = FullCode(s, l);
        if (await db.StoreLocations.AnyAsync(x => x.FullCode == l.FullCode && x.Id != l.Id, ct)) throw AppException.Bad($"Location {l.FullCode} already exists.");
        audit.Log("master", created ? "location-created" : "location-updated", me, "store", s.Code, next: new { l.FullCode, l.Kind, l.Active });
        await db.SaveChangesAsync(ct);
        return l.Id;
    }

    /// <summary>Bin labels (FR-INV-012): one QR per location, scanned with the item when putting away or moving stock.</summary>
    public async Task<List<object>> LocationLabelsAsync(int storeId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var s = await db.Stores.AsNoTracking().Include(x => x.Locations).FirstOrDefaultAsync(x => x.Id == storeId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, s);
        return s.Locations.Where(l => l.Active).OrderBy(l => l.FullCode).Select(l => (object)new { l.Id, l.FullCode, l.Kind, Payload = Codes.LocationPayload(l.FullCode) }).ToList();
    }

    // ---- sites -------------------------------------------------------------------------------
    public async Task<List<SiteDto>> SitesAsync(string? zone, string? q, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
        var stats = await db.Items.Where(i => i.SiteId != null).GroupBy(i => i.SiteId!.Value).Select(g => new { Site = g.Key, Installed = g.Count(i => i.Status == ItemStatus.Installed), AtSite = g.Count(i => i.Status == ItemStatus.Issued || i.Status == ItemStatus.AtSite), Pending = g.Count(i => i.Status == ItemStatus.PendingVerification) }).ToDictionaryAsync(x => x.Site, ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        string? N(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : null;
        return sites.Where(s => AccessPolicy.SiteVisible(me, s) && (string.IsNullOrEmpty(zone) || s.Zone == zone) && (string.IsNullOrWhiteSpace(q) || (s.Code + " " + s.Name + " " + s.Division + " " + (s.Ward ?? "")).Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Select(s => { stats.TryGetValue(s.Id, out var st); return new SiteDto(s.Id, s.Code, s.Name, s.Zone, s.Division, s.Ward, s.Type, s.Lat, s.Lng, s.GeofenceM, s.SupervisorUserId, N(s.SupervisorUserId), s.OfficerUserId, N(s.OfficerUserId), s.Status, s.Remarks, st?.Installed ?? 0, st?.AtSite ?? 0, st?.Pending ?? 0, s.CreatedAt); }).ToList();
    }

    public async Task<int> UpsertSiteAsync(int? id, SiteUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Configure, Permissions.Requisition);
        var cfg = await configs.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Zone) || string.IsNullOrWhiteSpace(r.Division)) throw AppException.Bad("Name, zone and division are required.");
        if (r.Lat is < -90 or > 90 || r.Lng is < -180 or > 180 || (r.Lat == 0 && r.Lng == 0)) throw AppException.Bad("Latitude and longitude are required (six decimal places).");
        var s = id is null ? null : await db.Sites.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && s is null) throw AppException.NotFound("Site");
        var created = s is null;
        object? prev = null;
        if (s is null) { s = new Site { Code = string.IsNullOrWhiteSpace(r.Code) ? await NextSiteCodeAsync(r.Zone, r.Division, ct) : Codes.Clean(r.Code, 24) }; db.Sites.Add(s); }
        else
        {
            UserContext.RequireSite(me, s);
            prev = new { s.Name, s.Lat, s.Lng, s.GeofenceM, s.Status };
            var moved = Math.Abs(s.Lat - r.Lat) > 1e-6 || Math.Abs(s.Lng - r.Lng) > 1e-6;
            if (moved && !AccessPolicy.Can(me, Permissions.Configure) && await db.Items.AnyAsync(i => i.SiteId == s.Id, ct))
                throw AppException.Rule("BR-025", "Coordinates of a site that already has material issued to it can only be changed by a configuring role. Ask the Store In-Charge or PD Office.");
        }
        s.Name = r.Name.Trim(); s.Zone = r.Zone; s.Division = r.Division; s.Ward = r.Ward; s.Type = r.Type; s.Lat = r.Lat; s.Lng = r.Lng; s.GeofenceM = r.GeofenceM is > 0 ? r.GeofenceM.Value : cfg.Geo.DefaultGeofenceM;
        s.SupervisorUserId = string.IsNullOrWhiteSpace(r.SupervisorUserId) ? (created && me.RoleKey == "supervisor" ? me.Id : null) : r.SupervisorUserId; s.OfficerUserId = string.IsNullOrWhiteSpace(r.OfficerUserId) ? null : r.OfficerUserId;
        s.Status = r.Status; s.Remarks = r.Remarks; s.UpdatedAt = DateTime.UtcNow;
        audit.Log("master", created ? "site-created" : "site-updated", me, "site", s.Code, prev, new { s.Name, s.Lat, s.Lng, s.GeofenceM, s.Status });
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    async Task<string> NextSiteCodeAsync(string zone, string division, CancellationToken ct)
    {
        var z = Codes.Clean(zone, 1); var d = Codes.Clean(division, 4);
        var n = await numbers.NextAsync("SITE-" + z + d, null, ct, 4, fy: false);
        return n.Replace('/', '-');
    }

    public async Task<BulkResultDto> BulkSitesAsync(List<SiteUpsert> rows, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.Configure, ct);
        var res = new BulkResultDto(0, 0, new());
        int created = 0, updated = 0;
        foreach (var (r, i) in rows.Select((r, i) => (r, i)))
        {
            try
            {
                var existing = string.IsNullOrWhiteSpace(r.Code) ? null : await db.Sites.FirstOrDefaultAsync(s => s.Code == Codes.Clean(r.Code!, 24), ct);
                await UpsertSiteAsync(existing?.Id, r, ct);
                if (existing is null) created++; else updated++;
            }
            catch (AppException e) { res.Errors.Add($"Row {i + 1} ({r.Code ?? r.Name}): {e.Message}"); }
        }
        return res with { Created = created, Updated = updated };
    }

    // ---- vendors & purchase orders ----------------------------------------------------------
    public async Task<List<VendorDto>> VendorsAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var pos = await db.PurchaseOrders.GroupBy(p => p.VendorId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return (await db.Vendors.AsNoTracking().OrderBy(v => v.Name).ToListAsync(ct)).Select(v => Map.Vendor(v, pos.GetValueOrDefault(v.Id))).ToList();
    }
    public async Task<int> UpsertVendorAsync(int? id, VendorUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Configure);
        var code = Codes.Clean(r.Code, 10); if (code.Length < 2 || string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Vendor code and name are required.");
        var v = id is null ? null : await db.Vendors.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && v is null) throw AppException.NotFound("Vendor");
        if (await db.Vendors.AnyAsync(x => x.Code == code && x.Id != (id ?? 0), ct)) throw AppException.Bad($"Vendor code {code} already exists.");
        var created = v is null; if (v is null) { v = new Vendor(); db.Vendors.Add(v); }
        v.Code = code; v.Name = r.Name.Trim(); v.Contact = r.Contact; v.Phone = r.Phone; v.Email = r.Email; v.Active = r.Active;
        audit.Log("master", created ? "vendor-created" : "vendor-updated", me, "vendor", v.Code, next: new { v.Name, v.Active });
        await db.SaveChangesAsync(ct); return v.Id;
    }

    public async Task<List<PoDto>> PurchaseOrdersAsync(string? status, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var pos = await db.PurchaseOrders.AsNoTracking().Include(p => p.Vendor).Include(p => p.Lines).ThenInclude(l => l.Article).Where(p => string.IsNullOrEmpty(status) || p.Status == status).OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync(ct);
        var stats = await ArticleStatsAsync(ct);
        return pos.Select(p => PoDto(p, stats)).ToList();
    }
    static PoDto PoDto(PurchaseOrder p, Dictionary<int, ArticleStat> stats) => new(p.Id, p.Number, p.VendorId, p.Vendor?.Name ?? "", p.Date, p.DeliveryDue, p.WarrantyMonths, p.Status, p.Remarks, p.Lines.Sum(l => l.Qty * l.Rate),
        p.Lines.OrderBy(l => l.LineNo).Select(l => { var st = l.Article is null ? null : stats.GetValueOrDefault(l.Article.Id); return new PoLineDto(l.Id, l.LineNo, l.Category, l.SubCategory, l.Spec, l.Description, l.Uom, l.Qty, l.Rate, l.Article?.Id, l.Article?.ItemCode, st?.Received ?? 0, st?.Issued ?? 0); }).ToList());

    public async Task<int> UpsertPoAsync(int? id, PoUpsertRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Configure);
        if (string.IsNullOrWhiteSpace(r.Number)) throw AppException.Bad("PO number is required.");
        if (!await db.Vendors.AnyAsync(v => v.Id == r.VendorId && v.Active, ct)) throw AppException.Bad("Choose an active vendor.");
        if (r.Lines.Count == 0) throw AppException.Bad("A purchase order needs at least one line.");
        var p = id is null ? null : await db.PurchaseOrders.Include(x => x.Lines).ThenInclude(l => l.Article).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && p is null) throw AppException.NotFound("Purchase order");
        var num = r.Number.Trim().ToUpperInvariant();
        if (await db.PurchaseOrders.AnyAsync(x => x.Number == num && x.Id != (id ?? 0), ct)) throw AppException.Bad($"PO {num} already exists.");
        var created = p is null; if (p is null) { p = new PurchaseOrder { CreatedBy = me.Id }; db.PurchaseOrders.Add(p); }
        p.Number = num; p.VendorId = r.VendorId; p.Date = r.Date; p.DeliveryDue = r.DeliveryDue; p.WarrantyMonths = r.WarrantyMonths; p.Remarks = r.Remarks;
        // lines with an article already created keep their identity; the rest are replaced
        var keep = p.Lines.Where(l => l.Article is not null).ToList();
        if (r.Lines.Count < keep.Count) throw AppException.Bad("Lines that already have an article cannot be removed.");
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var src = r.Lines[i];
            if (string.IsNullOrWhiteSpace(src.Category) || string.IsNullOrWhiteSpace(src.Description) || src.Qty <= 0) throw AppException.Bad($"Line {i + 1}: category, description and a positive quantity are required.");
            var line = i < keep.Count ? keep[i] : null;
            if (line is null) { line = new PurchaseOrderLine(); p.Lines.Add(line); }
            else if (Codes.Clean(src.Category, 4) != line.Category || Codes.Clean(src.SubCategory, 4) != line.SubCategory || Codes.Clean(src.Spec, 6) != line.Spec || src.Description.Trim() != line.Description || src.Uom != line.Uom)
                // DF-04: the article, its item code and its printed labels were made from this specification
                throw AppException.Rule("FR-MD-05", $"Line {line.LineNo} already has article {line.Article!.ItemCode}; its category, specification, description and unit can no longer change. Only quantity and rate can.");
            else if (src.Qty < line.Qty && (await ArticleStatsAsync(ct)).GetValueOrDefault(line.Article!.Id)?.Received > src.Qty) throw AppException.Bad($"Line {line.LineNo}: quantity cannot go below what has already been received.");
            line.LineNo = i + 1; line.Category = Codes.Clean(src.Category, 4); line.SubCategory = Codes.Clean(src.SubCategory, 4); line.Spec = Codes.Clean(src.Spec, 6); line.Description = src.Description.Trim(); line.Uom = src.Uom; line.Qty = src.Qty; line.Rate = src.Rate;
            if (line.Article is not null) { line.Article.Rate = src.Rate; line.Article.WarrantyMonths = p.WarrantyMonths; }
        }
        var extra = p.Lines.Where(l => l.Article is null).Skip(r.Lines.Count - keep.Count).ToList();
        foreach (var e in extra) { p.Lines.Remove(e); db.PurchaseOrderLines.Remove(e); }
        audit.Log("master", created ? "po-created" : "po-updated", me, "po", p.Number, next: new { p.Number, p.VendorId, Lines = p.Lines.Count, Value = p.Lines.Sum(l => l.Qty * l.Rate) });
        await db.SaveChangesAsync(ct); return p.Id;
    }

    public async Task ClosePoAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Configure);
        var p = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Purchase order");
        p.Status = p.Status == "Open" ? "Closed" : "Open";
        audit.Log("master", "po-" + p.Status.ToLowerInvariant(), me, "po", p.Number);
        await db.SaveChangesAsync(ct);
    }

    // ---- articles & QR series ---------------------------------------------------------------
    record ArticleStat(decimal Received, decimal OnHand, decimal Reserved, decimal Installed, decimal Quarantine, decimal Issued);
    async Task<Dictionary<int, ArticleStat>> ArticleStatsAsync(CancellationToken ct)
    {
        var rows = await db.Items.GroupBy(i => i.ArticleId).Select(g => new
        {
            g.Key,
            Received = g.Where(i => i.Status != ItemStatus.Created && i.Status != ItemStatus.Rejected && i.Status != ItemStatus.Cancelled).Sum(i => i.Qty),
            OnHand = g.Where(i => i.Status == ItemStatus.Received || i.Status == ItemStatus.Reserved).Sum(i => i.Qty),
            Reserved = g.Where(i => i.Status == ItemStatus.Reserved).Sum(i => i.Qty),
            Installed = g.Where(i => i.Status == ItemStatus.Installed).Sum(i => i.Qty),
            Quarantine = g.Where(i => i.Status == ItemStatus.Returned || i.Status == ItemStatus.UnderRepair || i.Status == ItemStatus.WarrantyClaim || i.Status == ItemStatus.Scrap).Sum(i => i.Qty),
            Issued = g.Where(i => i.Status == ItemStatus.Issued || i.Status == ItemStatus.AtSite || i.Status == ItemStatus.PendingVerification || i.Status == ItemStatus.Installed || i.Status == ItemStatus.OutOfOrder || i.Status == ItemStatus.Dismantled).Sum(i => i.Qty),
        }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Key, x => new ArticleStat(x.Received, x.OnHand, x.Reserved, x.Installed, x.Quarantine, x.Issued));
    }

    public async Task<List<ArticleDto>> ArticlesAsync(string? q, string? status, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var list = await db.Articles.AsNoTracking().Include(a => a.PurchaseOrder).Include(a => a.Vendor).Include(a => a.PurchaseOrderLine).OrderByDescending(a => a.Id).ToListAsync(ct);
        var stats = await ArticleStatsAsync(ct);
        return list.Where(a => (string.IsNullOrEmpty(status) || a.Status == status) && (string.IsNullOrWhiteSpace(q) || (a.ItemCode + " " + a.Description + " " + (a.PurchaseOrder?.Number ?? "")).Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Select(a => { var s = stats.GetValueOrDefault(a.Id); return Map.Article(a, a.PurchaseOrderLine?.Qty ?? 0, s?.Received ?? 0, s?.OnHand ?? 0, s?.Reserved ?? 0, s?.Installed ?? 0, s?.Quarantine ?? 0); }).ToList();
    }

    /// <summary>FR-MD-05/06: one article per PO line, item-code wise, with its QR series generated at once.</summary>
    public async Task<ArticleDto> CreateArticleAsync(ArticleCreateRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Configure, Permissions.Receive);
        var line = await db.PurchaseOrderLines.Include(l => l.PurchaseOrder).ThenInclude(p => p!.Vendor).Include(l => l.Article).FirstOrDefaultAsync(l => l.Id == r.PurchaseOrderLineId, ct) ?? throw AppException.NotFound("PO line");
        if (line.Article is not null) throw AppException.Bad($"Article {line.Article.ItemCode} already exists for this PO line.");
        var po = line.PurchaseOrder!;
        if (po.Status != "Open") throw AppException.Bad("The purchase order is closed.");
        var pack = r.Serialised ? 1 : (r.PackSize <= 0 ? 1 : r.PackSize);
        var count = r.Serialised ? (int)Math.Ceiling(line.Qty) : (int)Math.Ceiling(line.Qty / pack);
        if (count < 1 || count > 20000) throw AppException.Bad($"This would generate {count} labels; the limit is 20,000 per article. Use a larger pack size.");
        var a = new Article
        {
            ItemCode = Codes.ItemCode(line.Category, line.SubCategory, line.Spec, po.Number, line.LineNo), Category = line.Category, SubCategory = line.SubCategory, Spec = line.Spec, Description = line.Description, Uom = line.Uom,
            Serialised = r.Serialised, PackSize = pack, PurchaseOrderId = po.Id, PurchaseOrderLineId = line.Id, VendorId = po.VendorId, Rate = line.Rate, WarrantyMonths = po.WarrantyMonths, ExpectedLifeMonths = r.ExpectedLifeMonths,
            ReorderLevel = r.ReorderLevel, ItemsGenerated = count, CreatedBy = me.Id,
        };
        if (await db.Articles.AnyAsync(x => x.ItemCode == a.ItemCode, ct)) throw AppException.Bad($"Item code {a.ItemCode} already exists.");
        db.Articles.Add(a);
        var remaining = line.Qty;
        for (var s = 1; s <= count; s++)
        {
            var qty = r.Serialised ? 1 : Math.Min(pack, remaining); remaining -= qty;
            db.Items.Add(new Item { Id = Guid.NewGuid(), ArticleId = 0, Article = a, Serial = s, Qty = qty, QrCode = Codes.QrPayload(a.ItemCode, s), Status = ItemStatus.Created, UpdatedBy = me.Id });
        }
        audit.Log("master", "article-created", me, "article", a.ItemCode, next: new { a.ItemCode, po.Number, line.LineNo, Labels = count, a.Serialised, a.PackSize });
        await db.SaveChangesAsync(ct);
        a.PurchaseOrder = po; a.Vendor = po.Vendor;
        return Map.Article(a, line.Qty, 0, 0, 0, 0, 0);
    }

    public async Task UpdateArticleAsync(int id, decimal reorderLevel, string status, int? expectedLife, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Configure);
        var a = await db.Articles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Article");
        if (status is not ("Active" or "Blocked" or "Obsolete")) throw AppException.Bad("Status must be Active, Blocked or Obsolete.");
        audit.Log("master", "article-updated", me, "article", a.ItemCode, new { a.ReorderLevel, a.Status }, new { reorderLevel, status });
        a.ReorderLevel = reorderLevel; a.Status = status; a.ExpectedLifeMonths = expectedLife;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ItemDto>> ArticleItemsAsync(int articleId, string? status, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var q = Map.Items(db).AsNoTracking().Where(i => i.ArticleId == articleId);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ItemStatus>(status, true, out var st)) q = q.Where(i => i.Status == st);
        return (await q.OrderBy(i => i.Serial).ToListAsync(ct)).Select(Map.Item).ToList();
    }

    public async Task<ItemDetailDto> ItemAsync(string idOrQr, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        Item? item = Guid.TryParse(idOrQr, out var g) ? await Map.Items(db).AsNoTracking().FirstOrDefaultAsync(i => i.Id == g, ct) : null;
        item ??= await Map.Items(db).AsNoTracking().FirstOrDefaultAsync(i => i.QrCode == idOrQr, ct);
        if (item is null) throw AppException.NotFound("Item");
        var history = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == "item" && a.EntityId == item.Id.ToString()).OrderBy(a => a.Id)
            .Select(a => new ItemHistoryDto(a.At, a.Action, a.ActorName, a.Role, a.Reason, a.NextJson)).ToListAsync(ct);
        var inst = item.InstallationId is null ? null : await Map.Installations(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.InstallationId, ct);
        var ret = item.ReturnNoteId is null ? null : await Map.Returns(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.ReturnNoteId, ct);
        return new ItemDetailDto(Map.Item(item), history, inst is null ? null : Map.Installation(inst), ret is null ? null : Map.Return(ret));
    }

    /// <summary>FR-MD-14: a reprint keeps the same code and is recorded against the item.</summary>
    public async Task<List<ItemDto>> LabelsAsync(int articleId, int? fromSerial, int? toSerial, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Configure, Permissions.Procure);
        var items = await Map.Items(db).Where(i => i.ArticleId == articleId && (fromSerial == null || i.Serial >= fromSerial) && (toSerial == null || i.Serial <= toSerial)).OrderBy(i => i.Serial).ToListAsync(ct);
        if (items.Count == 0) throw AppException.NotFound("Items");
        foreach (var i in items) { i.LabelPrints += 1; if (i.LabelPrints > 1) audit.Log("master", "label-reprinted", me, "item", i.Id.ToString(), reason: $"print #{i.LabelPrints}"); }
        audit.Log("master", "labels-printed", me, "article", items[0].Article!.ItemCode, next: new { From = items[0].Serial, To = items[^1].Serial, Count = items.Count });
        await db.SaveChangesAsync(ct);
        return items.Select(Map.Item).ToList();
    }
}
