using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Inside the store: put-away and bin moves, inter-store transfers, adjustments with second-person approval and
/// scan-based physical verification (FR-INV-010, 012, 019, 020, 021).</summary>
public class StoreControlService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, NumberingService numbers, LedgerService ledger, NotificationService notify)
{
    /// <summary>Statuses in which an item physically sits in a store location.</summary>
    public static readonly ItemStatus[] InStore = [ItemStatus.Received, ItemStatus.Reserved, ItemStatus.Returned, ItemStatus.UnderRepair, ItemStatus.WarrantyClaim, ItemStatus.Scrap];

    async Task<Item> ItemByQrAsync(string? qr, CancellationToken ct)
    {
        if (!Codes.TryParse(qr, out var code, out var serial)) throw AppException.Rule("BR-002", "Not a SLIMS label — the QR payload or its check value is invalid.");
        return await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == qr!.Trim(), ct) ?? throw AppException.NotFound($"Item {code} #{serial}");
    }

    async Task<StoreLocation> LocationAsync(int? id, string? qr, CancellationToken ct)
    {
        var code = Codes.ParseLocation(qr);
        var loc = id is not null ? await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.Id == id, ct)
            : code is not null ? await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.FullCode == code, ct) : null;
        return loc ?? throw AppException.Rule("BR-005", "Scan or choose the bin the material is going to.");
    }

    /// <summary>What a scanned code is: an item label or a bin label. Lets one scan box drive the put-away screen.</summary>
    public async Task<ScanLookupDto> LookupAsync(string qr, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var locCode = Codes.ParseLocation(qr);
        if (locCode is not null)
        {
            var l = await db.StoreLocations.Include(x => x.Store).AsNoTracking().FirstOrDefaultAsync(x => x.FullCode == locCode, ct);
            return l is null ? new("unknown", null, null, $"Bin {locCode} is not registered.") : new("location", null, Map.Location(l, 0), $"Bin {l.FullCode} ({l.Kind})");
        }
        if (!Codes.TryParse(qr, out var code, out var serial)) return new("unknown", null, null, "Not a SLIMS label.");
        var i = await Map.Items(db).AsNoTracking().FirstOrDefaultAsync(x => x.QrCode == qr.Trim(), ct);
        return i is null ? new("unknown", null, null, $"Label {code} #{serial} is not registered.") : new("item", Map.Item(i), null, $"{i.Article!.ItemCode} #{i.Serial} — {i.Status}{(i.StoreLocation is null ? "" : " at " + i.StoreLocation.FullCode)}");
    }

    // ---- receiving area, put-away and bin moves -------------------------------------------------------
    public async Task<List<ItemDto>> ReceivingAsync(int? storeId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var rows = await Map.Items(db).AsNoTracking().Where(i => i.StoreLocation!.Kind == "Receiving" && (storeId == null || i.StoreLocation.StoreId == storeId) && (i.Status == ItemStatus.Received || i.Status == ItemStatus.Reserved))
            .OrderBy(i => i.ReceivedAt).ThenBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).ToListAsync(ct);
        return rows.Where(i => AccessPolicy.StoreVisible(me, i.StoreLocation!.Store!)).Select(Map.Item).ToList();
    }

    /// <summary>FR-INV-010/012: put away from Receiving, or move between bins, by scanning each item and the destination bin.
    /// Both locations go to the ledger; the store's quantity does not change.</summary>
    public async Task<MoveResultDto> MoveAsync(MoveRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Issue);
        var to = await LocationAsync(r.ToLocationId, r.ToLocationQr, ct);
        UserContext.RequireStore(me, to.Store!);
        if (!to.Active) throw AppException.Rule("BR-005", $"Bin {to.FullCode} is inactive.");
        if (to.Kind == "Receiving") throw AppException.Rule("BR-005", "Material cannot be moved back into the Receiving area.");
        var qrs = (r.QrCodes ?? []).Select(q => q.Trim()).Where(q => q != "").Distinct().ToArray();
        if (qrs.Length == 0) throw AppException.Rule("BR-001", "Scan at least one item.");
        var moved = new List<Item>();
        foreach (var qr in qrs)
        {
            var i = await ItemByQrAsync(qr, ct);
            if (i.StoreLocation is null || !InStore.Contains(i.Status)) throw AppException.Rule("BR-013", $"{i.Article!.ItemCode} #{i.Serial} is {i.Status} and not in a store.");
            if (i.StoreLocation.StoreId != to.StoreId) throw AppException.Rule("FR-INV-019", $"{i.Article!.ItemCode} #{i.Serial} is in store {i.StoreLocation.Store?.Code}; use a transfer to move it to {to.Store!.Code}.");
            if (i.StoreLocationId == to.Id) continue;
            var goodStatus = to.Kind switch { "Stock" => i.Status is ItemStatus.Received or ItemStatus.Reserved, "Quarantine" => i.Status is ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim, "Scrap" => i.Status == ItemStatus.Scrap, _ => false };
            if (!goodStatus) throw AppException.Rule("BR-018", $"{i.Article!.ItemCode} #{i.Serial} is {i.Status}; it cannot go into a {to.Kind} location.");
            var from = i.StoreLocation;
            i.StoreLocationId = to.Id; i.StoreLocation = to; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id;
            ledger.Add("Move", i, to.StoreId, 0, me.Id, from.Id, to.Id, "move", from.Kind == "Receiving" ? "put-away" : "bin move");
            audit.Log("stock", from.Kind == "Receiving" ? "put-away" : "moved", me, "item", i.Id.ToString(), new { Location = from.FullCode }, new { Location = to.FullCode }, r.Remarks);
            moved.Add(i);
        }
        await db.SaveChangesAsync(ct);
        return new(moved.Count, to.FullCode, moved.Select(Map.Item).ToList());
    }

    // ---- inter-store transfer ------------------------------------------------------------------------
    public async Task<TransferDto> DespatchAsync(TransferDespatchRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Issue, ct);
        var from = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.FromStoreId, ct) ?? throw AppException.NotFound("Despatching store");
        var to = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.ToStoreId && s.Active, ct) ?? throw AppException.NotFound("Receiving store");
        UserContext.RequireStore(me, from);
        if (from.Id == to.Id) throw AppException.Bad("Choose two different stores.");
        var qrs = (r.QrCodes ?? []).Select(q => q.Trim()).Where(q => q != "").Distinct().ToArray();
        if (qrs.Length == 0) throw AppException.Rule("BR-001", "Scan at least one item to despatch.");
        var items = new List<Item>();
        foreach (var qr in qrs)
        {
            var i = await ItemByQrAsync(qr, ct);
            if (i.Status != ItemStatus.Received || i.StoreLocation?.StoreId != from.Id || i.StoreLocation.Kind is not ("Stock" or "Receiving"))
                throw AppException.Rule("BR-011", $"{i.Article!.ItemCode} #{i.Serial} is {i.Status}{(i.StoreLocation is null ? "" : " at " + i.StoreLocation.FullCode)}; only unreserved stock of {from.Code} can be transferred.");
            items.Add(i);
        }
        var t = new StockTransfer { Number = await numbers.NextAsync("STN", from.Code, ct), FromStoreId = from.Id, ToStoreId = to.Id, DespatchedById = me.Id, DespatchedBy = me.Name, Vehicle = r.Vehicle, Remarks = r.Remarks, ItemCount = items.Count, Qty = items.Sum(i => i.Qty) };
        db.StockTransfers.Add(t);
        await db.SaveChangesAsync(ct);
        foreach (var i in items)
        {
            var fromLoc = i.StoreLocation!;
            i.Status = ItemStatus.InTransit; i.StoreLocationId = null; i.TransferId = t.Id; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id;
            ledger.Add("TransferOut", i, from.Id, -i.Qty, me.Id, fromLoc.Id, null, "transfer", t.Number);
            audit.Log("stock", "transfer-despatched", me, "item", i.Id.ToString(), new { Location = fromLoc.FullCode }, new { Transfer = t.Number, To = to.Code });
        }
        audit.Log("stock", "transfer-despatched", me, "transfer", t.Number, next: new { From = from.Code, To = to.Code, t.ItemCount, t.Qty, t.Vehicle });
        await notify.NotifyAsync(await notify.StoreStaffAsync(to, ct), "NTF-05", $"Transfer {t.Number} on its way", $"{t.ItemCount} item(s) despatched from {from.Code} to {to.Code}. Scan them in on arrival.", "/transfers", "transfer", t.Number, ct);
        await db.SaveChangesAsync(ct);
        return await TransferAsync(t.Id, ct);
    }

    /// <summary>The receipt leg: each item is scanned in at the receiving store and lands in its Receiving area for put-away.</summary>
    public async Task<TransferDto> ReceiveTransferAsync(int id, TransferReceiveRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Receive, ct);
        var t = await db.StockTransfers.Include(x => x.ToStore).Include(x => x.FromStore).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Transfer");
        UserContext.RequireStore(me, t.ToStore!);
        var recv = await ReceivingLocationAsync(t.ToStoreId, ct);
        var qrs = (r.QrCodes ?? []).Select(q => q.Trim()).Where(q => q != "").Distinct().ToArray();
        if (qrs.Length == 0) throw AppException.Rule("BR-001", "Scan the items that arrived.");
        foreach (var qr in qrs)
        {
            var i = await ItemByQrAsync(qr, ct);
            if (i.TransferId != t.Id || i.Status != ItemStatus.InTransit) throw AppException.Rule("FR-INV-019", $"{i.Article!.ItemCode} #{i.Serial} is not in transit on {t.Number}.");
            i.Status = ItemStatus.Received; i.StoreLocationId = recv.Id; i.TransferId = null; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id;
            ledger.Add("TransferIn", i, t.ToStoreId, i.Qty, me.Id, null, recv.Id, "transfer", t.Number);
            audit.Log("stock", "transfer-received", me, "item", i.Id.ToString(), next: new { Transfer = t.Number, Location = recv.FullCode });
        }
        await db.SaveChangesAsync(ct);
        var left = await db.Items.CountAsync(i => i.TransferId == t.Id && i.Status == ItemStatus.InTransit, ct);
        t.Status = left == 0 ? "Received" : "PartReceived"; t.ReceivedById = me.Id; t.ReceivedBy = me.Name; t.ReceivedAt = DateTime.UtcNow; t.Remarks = r.Remarks ?? t.Remarks;
        audit.Log("stock", "transfer-received", me, "transfer", t.Number, next: new { t.Status, Received = qrs.Length, StillInTransit = left });
        await db.SaveChangesAsync(ct);
        return await TransferAsync(t.Id, ct);
    }

    public async Task<StoreLocation> ReceivingLocationAsync(int storeId, CancellationToken ct)
    {
        var loc = await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.StoreId == storeId && l.Kind == "Receiving" && l.Active, ct);
        if (loc is not null) return loc;
        var store = await db.Stores.FirstAsync(s => s.Id == storeId, ct);
        loc = new StoreLocation { StoreId = storeId, Store = store, Section = "RCV", Rack = "R0", Bin = "B0", Kind = "Receiving", FullCode = $"{store.Code}-RCV-R0-B0" };
        db.StoreLocations.Add(loc);
        return loc;
    }

    public async Task<TransferDto> TransferAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.FromStore).Include(x => x.ToStore).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Transfer");
        if (!AccessPolicy.StoreVisible(me, t.FromStore!) && !AccessPolicy.StoreVisible(me, t.ToStore!)) throw AppException.Forbidden("This transfer is outside your scope.", "scope");
        var moved = await db.StockMovements.AsNoTracking().Where(m => m.RefNo == t.Number && m.Type == "TransferOut").Select(m => m.ItemId).ToListAsync(ct);
        var items = await Map.Items(db).AsNoTracking().Where(i => moved.Contains(i.Id)).OrderBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).ToListAsync(ct);
        return ToDto(t, items);
    }

    public async Task<List<TransferDto>> TransfersAsync(string? status, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var rows = await db.StockTransfers.AsNoTracking().Include(x => x.FromStore).Include(x => x.ToStore).Where(x => string.IsNullOrEmpty(status) || x.Status == status).OrderByDescending(x => x.Id).Take(300).ToListAsync(ct);
        var transit = await db.Items.Where(i => i.TransferId != null && i.Status == ItemStatus.InTransit).GroupBy(i => i.TransferId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return rows.Where(t => AccessPolicy.StoreVisible(me, t.FromStore!) || AccessPolicy.StoreVisible(me, t.ToStore!)).Select(t => ToDto(t, new(), transit.GetValueOrDefault(t.Id))).ToList();
    }

    static TransferDto ToDto(StockTransfer t, List<Item> items, int? inTransit = null) => new(t.Id, t.Number, t.FromStoreId, t.FromStore?.Code ?? "", t.ToStoreId, t.ToStore?.Code ?? "", t.Status, t.DespatchedBy, t.DespatchedAt, t.Vehicle, t.Remarks,
        t.ItemCount, t.Qty, inTransit ?? items.Count(i => i.Status == ItemStatus.InTransit), t.ReceivedBy, t.ReceivedAt, items.Select(Map.Item).ToList());

    // ---- adjustments -----------------------------------------------------------------------------------
    public async Task<AdjustmentDto> RaiseAdjustmentAsync(AdjustmentRequest r, CancellationToken ct, int? countId = null)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Issue, Permissions.Approve);
        var cfg = await configs.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(r.ReasonCode)) throw AppException.Bad("Choose a reason code.");
        if (!cfg.AdjustmentReasons.Contains(r.ReasonCode)) throw AppException.Bad($"Unknown reason code \"{r.ReasonCode}\". Use one from the list under Parameters.");
        var i = await ItemByQrAsync(r.Qr, ct);
        if (await db.StockAdjustments.AnyAsync(a => a.ItemId == i.Id && a.Status == "Pending", ct)) throw AppException.Bad($"An adjustment for {i.Article!.ItemCode} #{i.Serial} is already awaiting approval.");
        Store store; decimal after; int? locId;
        switch (r.Kind)
        {
            case "Loss":
                if (i.StoreLocation is null || !InStore.Contains(i.Status)) throw AppException.Rule("FR-INV-020", $"{i.Article!.ItemCode} #{i.Serial} is {i.Status}; only an item on the store's books can be written off.");
                if (i.Status == ItemStatus.Reserved) throw AppException.Rule("BR-009", $"The item is reserved for a requisition. Release the reservation first.");
                store = i.StoreLocation.Store!; after = 0; locId = i.StoreLocationId; break;
            case "Found":
                if (i.Status != ItemStatus.WrittenOff) throw AppException.Rule("FR-INV-020", $"{i.Article!.ItemCode} #{i.Serial} is {i.Status}; only a written-off item can be brought back as found.");
                var loc = await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.Id == r.LocationId && l.Kind == "Stock" && l.Active, ct) ?? throw AppException.Rule("BR-005", "Choose the stock bin where the item was found.");
                store = loc.Store!; after = i.Qty; locId = loc.Id; break;
            case "Quantity":
                if (i.Article!.Serialised) throw AppException.Bad("Serialised items are adjusted as Loss or Found, one label at a time.");
                if (i.StoreLocation is null || i.Status != ItemStatus.Received) throw AppException.Rule("FR-INV-020", $"Pack {i.Article.ItemCode} #{i.Serial} is {i.Status}; only unreserved stock can be corrected.");
                if (r.QtyAfter is null || r.QtyAfter < 0) throw AppException.Bad("Enter the counted quantity.");
                if (r.QtyAfter == i.Qty) throw AppException.Bad("The counted quantity equals the book quantity; nothing to adjust.");
                store = i.StoreLocation.Store!; after = r.QtyAfter.Value; locId = i.StoreLocationId; break;
            default: throw AppException.Bad("Kind must be Loss, Found or Quantity.");
        }
        UserContext.RequireStore(me, store);
        var a = new StockAdjustment { Number = await numbers.NextAsync("ADJ", store.Code, ct), StoreId = store.Id, ItemId = i.Id, ArticleId = i.ArticleId, LocationId = locId, Kind = r.Kind, ReasonCode = r.ReasonCode, Remarks = r.Remarks,
            QtyBefore = r.Kind == "Found" ? 0 : i.Qty, QtyAfter = after, PhysicalCountId = countId, RaisedById = me.Id, RaisedBy = me.Name };
        db.StockAdjustments.Add(a);
        audit.Log("stock", "adjustment-raised", me, "adjustment", a.Number, new { a.QtyBefore }, new { Item = i.Article!.ItemCode + " #" + i.Serial, a.Kind, a.ReasonCode, a.QtyAfter }, r.Remarks);
        var approvers = (await notify.RoleAsync("storeincharge", ct, store)).Concat(await notify.RoleAsync("pdoffice", ct, store)).Where(u => u.Id != me.Id);
        await notify.NotifyAsync(approvers, "NTF-13", $"Adjustment {a.Number} awaiting approval", $"{a.Kind} of {i.Article.ItemCode} #{i.Serial} at {store.Code}: {a.QtyBefore:0.###} → {a.QtyAfter:0.###} ({a.ReasonCode}). Raised by {me.Name}.", "/adjustments", "adjustment", a.Number, ct);
        await db.SaveChangesAsync(ct);
        return (await AdjustmentsAsync(null, ct, a.Id)).Single();
    }

    /// <summary>Second-person approval. Only an approved adjustment changes the book; the original and adjusted values stay on the record.</summary>
    public async Task<AdjustmentDto> DecideAdjustmentAsync(int id, DecisionRequest d, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Approve, ct);
        var a = await db.StockAdjustments.Include(x => x.Store).Include(x => x.Item).ThenInclude(i => i!.Article).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Adjustment");
        UserContext.RequireStore(me, a.Store!);
        if (a.Status != "Pending") throw AppException.Bad($"Adjustment is already {a.Status}.");
        if (a.RaisedById == me.Id) throw AppException.Rule("BR-026", "An adjustment must be approved by someone other than the person who raised it.");
        a.DecidedById = me.Id; a.DecidedBy = me.Name; a.DecidedAt = DateTime.UtcNow; a.DecisionNote = d.Note;
        var i = a.Item!;
        if (!d.Approve)
        {
            if (string.IsNullOrWhiteSpace(d.Note)) throw AppException.Bad("A reason is required to reject.");
            a.Status = "Rejected";
            audit.Log("stock", "adjustment-rejected", me, "adjustment", a.Number, reason: d.Note);
        }
        else
        {
            var prev = new { Status = i.Status.ToString(), i.Qty, i.StoreLocationId };
            switch (a.Kind)
            {
                case "Loss":
                    if (!InStore.Contains(i.Status) || i.Status == ItemStatus.Reserved) throw AppException.Rule("FR-INV-020", $"The item is now {i.Status}; the adjustment no longer applies. Reject it.");
                    ledger.Add("Adjustment", i, a.StoreId, -i.Qty, me.Id, i.StoreLocationId, null, "adjustment", a.Number);
                    i.Status = ItemStatus.WrittenOff; i.StoreLocationId = null; break;
                case "Found":
                    if (i.Status != ItemStatus.WrittenOff) throw AppException.Rule("FR-INV-020", $"The item is now {i.Status}; the adjustment no longer applies. Reject it.");
                    i.Status = ItemStatus.Received; i.StoreLocationId = a.LocationId; i.SiteId = null; i.RequisitionId = null;
                    ledger.Add("Adjustment", i, a.StoreId, i.Qty, me.Id, null, a.LocationId, "adjustment", a.Number); break;
                case "Quantity":
                    if (i.Status != ItemStatus.Received || i.Qty != a.QtyBefore) throw AppException.Rule("FR-INV-020", "The pack has changed since the adjustment was raised. Reject it and count again.");
                    ledger.Add("Adjustment", i, a.StoreId, a.QtyAfter - a.QtyBefore, me.Id, i.StoreLocationId, i.StoreLocationId, "adjustment", a.Number);
                    i.Qty = a.QtyAfter;
                    if (a.QtyAfter == 0) { i.Status = ItemStatus.WrittenOff; i.StoreLocationId = null; }
                    break;
            }
            i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id;
            a.Status = "Approved";
            audit.Log("stock", "adjusted", me, "item", i.Id.ToString(), prev, new { Status = i.Status.ToString(), i.Qty, i.StoreLocationId, Adjustment = a.Number }, a.ReasonCode);
            audit.Log("stock", "adjustment-approved", me, "adjustment", a.Number, new { a.QtyBefore }, new { a.QtyAfter }, d.Note);
        }
        await notify.NotifyAsync(await notify.ByIdsAsync(ct, a.RaisedById), "NTF-13", $"Adjustment {a.Number} {a.Status.ToLowerInvariant()}", $"{a.Kind} of {i.Article!.ItemCode} #{i.Serial} was {a.Status.ToLowerInvariant()} by {me.Name}. {d.Note}", "/adjustments", "adjustment", a.Number, ct, level: 9);
        await db.SaveChangesAsync(ct);
        return (await AdjustmentsAsync(null, ct, a.Id)).Single();
    }

    public async Task<List<AdjustmentDto>> AdjustmentsAsync(string? status, CancellationToken ct, int? id = null)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var rows = await db.StockAdjustments.AsNoTracking().Include(x => x.Store).Include(x => x.Item).ThenInclude(i => i!.Article)
            .Where(x => (id == null || x.Id == id) && (string.IsNullOrEmpty(status) || x.Status == status)).OrderByDescending(x => x.Id).Take(500).ToListAsync(ct);
        return rows.Where(a => AccessPolicy.StoreVisible(me, a.Store!)).Select(a => new AdjustmentDto(a.Id, a.Number, a.StoreId, a.Store!.Code, a.ItemId, a.Item!.QrCode, a.Item.Article!.ItemCode, a.Item.Article.Description, a.Item.Serial,
            a.Kind, a.ReasonCode, a.Remarks, a.QtyBefore, a.QtyAfter, a.Status, a.PhysicalCountId, a.RaisedBy, a.RaisedById, a.RaisedAt, a.DecidedBy, a.DecidedAt, a.DecisionNote, a.Item.Status.ToString())).ToList();
    }

    // ---- physical verification ------------------------------------------------------------------------
    record Scan(string Qr, decimal Qty, int? LocationId, DateTime At);
    record Variance(string Kind, Guid? ItemId, string Qr, string? ItemCode, int? Serial, string? BookLocation, string? CountedLocation, decimal BookQty, decimal CountedQty, int? AdjustmentId);

    public async Task<CountDto> StartCountAsync(CountStartRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Approve);
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.StoreId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, store);
        if (r.LocationId is not null && !await db.StoreLocations.AnyAsync(l => l.Id == r.LocationId && l.StoreId == store.Id, ct)) throw AppException.NotFound("Location");
        if (await db.PhysicalCounts.AnyAsync(c => c.StoreId == store.Id && c.Status == "Open" && (c.LocationId == null || r.LocationId == null || c.LocationId == r.LocationId), ct)) throw AppException.Bad("A count covering this area is already open. Finish it first.");
        var c = new PhysicalCount { Number = await numbers.NextAsync("PV", store.Code, ct), StoreId = store.Id, LocationId = r.LocationId, StartedById = me.Id, StartedBy = me.Name };
        db.PhysicalCounts.Add(c);
        audit.Log("stock", "count-started", me, "count", c.Number, next: new { Store = store.Code, c.LocationId });
        await db.SaveChangesAsync(ct);
        return await CountAsync(c.Id, ct);
    }

    async Task<List<Item>> BookAsync(PhysicalCount c, CancellationToken ct)
        => await Map.Items(db).AsNoTracking().Where(i => i.StoreLocation!.StoreId == c.StoreId && (c.LocationId == null || i.StoreLocationId == c.LocationId) && InStore.Contains(i.Status)).ToListAsync(ct);

    public async Task<CountScanDto> CountScanAsync(int id, CountScanRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Approve);
        var c = await db.PhysicalCounts.Include(x => x.Store).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Count");
        UserContext.RequireStore(me, c.Store!);
        if (c.Status != "Open") throw AppException.Bad("This count is no longer open.");
        var scans = JsonSerializer.Deserialize<List<Scan>>(c.ScansJson, ConfigService.Json) ?? new();
        var qr = (r.Qr ?? "").Trim();
        var item = Codes.TryParse(qr, out _, out _) ? await Map.Items(db).AsNoTracking().FirstOrDefaultAsync(i => i.QrCode == qr, ct) : null;
        var at = r.LocationId ?? c.LocationId;
        string result;
        if (item is null) result = "Not a registered SLIMS label";
        else if (scans.Any(s => s.Qr == qr)) result = "Already counted";
        else
        {
            scans.Add(new Scan(qr, r.Qty ?? item.Qty, at, DateTime.UtcNow));
            c.ScansJson = JsonSerializer.Serialize(scans, ConfigService.Json);
            var inScope = item.StoreLocation?.StoreId == c.StoreId && InStore.Contains(item.Status) && (c.LocationId == null || item.StoreLocationId == c.LocationId);
            result = !inScope ? $"Not on this store's books here ({item.Status}{(item.StoreLocation is null ? "" : " at " + item.StoreLocation.FullCode)})"
                : at is not null && item.StoreLocationId != at ? $"Belongs in {item.StoreLocation!.FullCode}" : (r.Qty is not null && r.Qty != item.Qty ? $"Quantity differs (book {item.Qty:0.###})" : "OK");
            await db.SaveChangesAsync(ct);
        }
        var loc = at is null ? null : await db.StoreLocations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == at, ct);
        return new CountScanDto(qr, item?.Id, item?.Article?.ItemCode, item?.Serial, r.Qty ?? item?.Qty ?? 0, at, loc?.FullCode, DateTime.UtcNow, result);
    }

    /// <summary>Compare what was scanned with book stock and freeze the variance report.</summary>
    public async Task<CountDto> SubmitCountAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Approve);
        var c = await db.PhysicalCounts.Include(x => x.Store).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Count");
        UserContext.RequireStore(me, c.Store!);
        if (c.Status != "Open") throw AppException.Bad("This count has already been submitted.");
        var scans = JsonSerializer.Deserialize<List<Scan>>(c.ScansJson, ConfigService.Json) ?? new();
        var book = await BookAsync(c, ct);
        var locs = await db.StoreLocations.AsNoTracking().Where(l => l.StoreId == c.StoreId).ToDictionaryAsync(l => l.Id, l => l.FullCode, ct);
        string? L(int? id) => id is not null && locs.TryGetValue(id.Value, out var s) ? s : null;
        var scanned = scans.ToDictionary(s => s.Qr);
        var lines = new List<Variance>();
        foreach (var i in book)
        {
            if (!scanned.TryGetValue(i.QrCode, out var s)) { lines.Add(new("Missing", i.Id, i.QrCode, i.Article!.ItemCode, i.Serial, i.StoreLocation!.FullCode, null, i.Qty, 0, null)); continue; }
            if (s.Qty != i.Qty) lines.Add(new("Quantity", i.Id, i.QrCode, i.Article!.ItemCode, i.Serial, i.StoreLocation!.FullCode, L(s.LocationId), i.Qty, s.Qty, null));
            else if (s.LocationId is not null && s.LocationId != i.StoreLocationId) lines.Add(new("Misplaced", i.Id, i.QrCode, i.Article!.ItemCode, i.Serial, i.StoreLocation!.FullCode, L(s.LocationId), i.Qty, s.Qty, null));
        }
        var bookQrs = book.Select(b => b.QrCode).ToHashSet();
        var extra = scans.Where(s => !bookQrs.Contains(s.Qr)).ToList();
        var extraItems = await Map.Items(db).AsNoTracking().Where(i => extra.Select(e => e.Qr).Contains(i.QrCode)).ToDictionaryAsync(i => i.QrCode, ct);
        foreach (var s in extra) { extraItems.TryGetValue(s.Qr, out var i); lines.Add(new("Unexpected", i?.Id, s.Qr, i?.Article?.ItemCode, i?.Serial, i?.StoreLocation?.FullCode ?? i?.Status.ToString(), L(s.LocationId), 0, s.Qty, null)); }
        c.VarianceJson = JsonSerializer.Serialize(lines, ConfigService.Json); c.Status = "Submitted"; c.SubmittedAt = DateTime.UtcNow;
        audit.Log("stock", "count-submitted", me, "count", c.Number, next: new { Book = book.Count, Scanned = scans.Count, Missing = lines.Count(l => l.Kind == "Missing"), Quantity = lines.Count(l => l.Kind == "Quantity"), Misplaced = lines.Count(l => l.Kind == "Misplaced"), Unexpected = lines.Count(l => l.Kind == "Unexpected") });
        await db.SaveChangesAsync(ct);
        return await CountAsync(id, ct);
    }

    /// <summary>Variances are cleared only through approved adjustments: raise one per missing item and per quantity difference.</summary>
    public async Task<CountDto> RaiseCountAdjustmentsAsync(int id, string reasonCode, CancellationToken ct)
    {
        var c = await db.PhysicalCounts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Count");
        if (c.Status != "Submitted") throw AppException.Bad("Submit the count first.");
        var lines = JsonSerializer.Deserialize<List<Variance>>(c.VarianceJson, ConfigService.Json) ?? new();
        for (var k = 0; k < lines.Count; k++)
        {
            var v = lines[k];
            if (v.AdjustmentId is not null || v.Kind is not ("Missing" or "Quantity")) continue;
            var dto = await RaiseAdjustmentAsync(new AdjustmentRequest(v.Qr, v.Kind == "Missing" ? "Loss" : "Quantity", reasonCode, v.Kind == "Missing" ? null : v.CountedQty, null, $"Physical verification {c.Number}"), ct, c.Id);
            lines[k] = v with { AdjustmentId = dto.Id };
            c.VarianceJson = JsonSerializer.Serialize(lines, ConfigService.Json);
            await db.SaveChangesAsync(ct);
        }
        return await CountAsync(id, ct);
    }

    public async Task<CountDto> CloseCountAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Approve);
        var c = await db.PhysicalCounts.Include(x => x.Store).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Count");
        UserContext.RequireStore(me, c.Store!);
        if (c.Status != "Submitted") throw AppException.Bad("Only a submitted count can be closed.");
        var dto = await CountAsync(id, ct);
        var open = dto.Variances.Where(v => v.Clearance == "Open").ToList();
        if (open.Any(v => v.Kind is "Missing" or "Quantity" && v.AdjustmentStatus is null or "Pending"))
            throw AppException.Rule("FR-INV-021", "Some variances have no decided adjustment yet. Raise adjustments and have them approved or rejected before closing.");
        c.Status = "Closed"; c.ClosedAt = DateTime.UtcNow;
        audit.Log("stock", "count-closed", me, "count", c.Number, next: new { Open = open.Count });
        await db.SaveChangesAsync(ct);
        return await CountAsync(id, ct);
    }

    public async Task<CountDto> CountAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var c = await db.PhysicalCounts.AsNoTracking().Include(x => x.Store).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Count");
        if (!AccessPolicy.StoreVisible(me, c.Store!)) throw AppException.Forbidden("This count is outside your scope.", "scope");
        var scans = JsonSerializer.Deserialize<List<Scan>>(c.ScansJson, ConfigService.Json) ?? new();
        var lines = JsonSerializer.Deserialize<List<Variance>>(c.VarianceJson, ConfigService.Json) ?? new();
        var locs = await db.StoreLocations.AsNoTracking().Where(l => l.StoreId == c.StoreId).ToDictionaryAsync(l => l.Id, l => l.FullCode, ct);
        var items = await Map.Items(db).AsNoTracking().Where(i => scans.Select(s => s.Qr).Contains(i.QrCode) || lines.Select(v => v.Qr).Contains(i.QrCode)).ToDictionaryAsync(i => i.QrCode, ct);
        var adjIds = lines.Where(v => v.AdjustmentId != null).Select(v => v.AdjustmentId!.Value).ToList();
        var adj = await db.StockAdjustments.AsNoTracking().Where(a => adjIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Status, ct);
        var bookCount = c.Status == "Open" ? (await BookAsync(c, ct)).Count : 0;
        string Clear(Variance v)
        {
            var st = v.AdjustmentId is null ? null : adj.GetValueOrDefault(v.AdjustmentId.Value);
            if (st == "Approved") return "Cleared by adjustment";
            items.TryGetValue(v.Qr, out var now);
            if (v.Kind == "Misplaced" && now is not null && locs.GetValueOrDefault(now.StoreLocationId ?? 0) == v.CountedLocation) return "Cleared by bin move";
            return "Open";
        }
        return new CountDto(c.Id, c.Number, c.StoreId, c.Store!.Code, c.LocationId, c.LocationId is null ? null : locs.GetValueOrDefault(c.LocationId.Value), c.Status, c.StartedBy, c.StartedAt, c.SubmittedAt, c.ClosedAt,
            bookCount, scans.Count,
            scans.OrderByDescending(s => s.At).Select(s => { items.TryGetValue(s.Qr, out var i); return new CountScanDto(s.Qr, i?.Id, i?.Article?.ItemCode, i?.Serial, s.Qty, s.LocationId, s.LocationId is null ? null : locs.GetValueOrDefault(s.LocationId.Value), s.At, ""); }).ToList(),
            lines.Select(v => new VarianceLineDto(v.Kind, v.ItemId, v.Qr, v.ItemCode, v.Serial, v.BookLocation, v.CountedLocation, v.BookQty, v.CountedQty, v.AdjustmentId, v.AdjustmentId is null ? null : adj.GetValueOrDefault(v.AdjustmentId.Value), Clear(v))).ToList());
    }

    public async Task<List<CountDto>> CountsAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var ids = await db.PhysicalCounts.AsNoTracking().Include(c => c.Store).OrderByDescending(c => c.Id).Take(100).ToListAsync(ct);
        var list = new List<CountDto>();
        foreach (var c in ids.Where(c => AccessPolicy.StoreVisible(me, c.Store!))) list.Add(await CountAsync(c.Id, ct));
        return list;
    }
}
