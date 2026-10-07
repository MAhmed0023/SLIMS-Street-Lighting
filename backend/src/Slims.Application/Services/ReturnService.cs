using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Phase 5 — out-of-order and dismantled items back to store, warranty, repair, condemnation (FR-RT-01 … FR-RT-12).</summary>
public class ReturnService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, NumberingService numbers, LedgerService ledger, NotificationService notify)
{
    /// <summary>FR-RT-01: the officer records the removal at site, with the failure reason.</summary>
    public async Task<ReturnDto> ReportAsync(ReturnReportRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Install, Permissions.Return);
        if (!Codes.TryParse(r.Qr, out _, out _)) throw AppException.Rule("BR-002", "Not a SLIMS label.");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == r.Qr.Trim(), ct) ?? throw AppException.NotFound("Item");
        if (item.Site is not null) UserContext.RequireSite(me, item.Site);
        if (await db.ReturnNotes.AnyAsync(x => x.ItemId == item.Id && x.ClosedAt == null && x.ReceivedAt == null, ct)) throw AppException.Bad("A return is already open for this item.");
        var kind = r.Kind is "Dismantled" or "Unused" ? r.Kind : "OutOfOrder";
        if (kind == "Unused") { if (item.Status is not (ItemStatus.Issued or ItemStatus.AtSite)) throw AppException.Bad($"Item is {item.Status}; only unused issued material can be returned as unused."); }
        else { if (item.Status != ItemStatus.Installed) throw AppException.Rule("BR-029", $"Item is {item.Status}; only an installed item can be reported out of order or dismantled."); }
        if (string.IsNullOrWhiteSpace(r.Reason)) throw AppException.Bad("A failure / removal reason is required.");
        var note = new ReturnNote { Number = await numbers.NextAsync("RTN", null, ct), ItemId = item.Id, SiteId = item.SiteId, Reason = r.Reason.Trim(), Kind = kind, ReportedById = me.Id, ReportedBy = me.Name, Remarks = r.Remarks };
        db.ReturnNotes.Add(note);
        await db.SaveChangesAsync(ct);
        if (kind != "Unused") { item.Status = kind == "Dismantled" ? ItemStatus.Dismantled : ItemStatus.OutOfOrder; }
        item.ReturnNoteId = note.Id; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        audit.Log("stock", kind == "Unused" ? "unused-return-reported" : kind == "Dismantled" ? "dismantled" : "out-of-order", me, "item", item.Id.ToString(), next: new { note.Number, Site = item.Site?.Code, note.Reason }, reason: r.Remarks);
        // NTF-11: the stores that serve the site's zone
        var zoneStores = await db.Stores.AsNoTracking().Where(s => s.Active && (item.Site == null || s.Zone == item.Site.Zone)).ToListAsync(ct);
        if (zoneStores.Count == 0) zoneStores = await db.Stores.AsNoTracking().Where(s => s.Active).ToListAsync(ct);
        var staff = new List<User>(); foreach (var s in zoneStores) staff.AddRange(await notify.StoreStaffAsync(s, ct));
        await notify.NotifyAsync(staff, "NTF-11", $"{(kind == "Unused" ? "Unused material" : "Out-of-order item")} coming back from {item.Site?.Code}", $"{item.Article!.ItemCode} #{item.Serial}: {note.Reason}. Reported by {me.Name} ({note.Number}).", "/returns", "return", note.Number, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(note.Id, ct);
    }

    /// <summary>FR-RT-02/03, BR-018: the store scans the item back in — into quarantine (failed) or stock (unused).</summary>
    public async Task<ReturnDto> ReceiveAsync(ReturnReceiveRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Return, ct);
        var store = await db.Stores.Include(s => s.Locations).FirstOrDefaultAsync(s => s.Id == r.StoreId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, store);
        if (!Codes.TryParse(r.Qr, out _, out _)) throw AppException.Rule("BR-002", "Not a SLIMS label.");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == r.Qr.Trim(), ct) ?? throw AppException.NotFound("Item");
        var note = await db.ReturnNotes.FirstOrDefaultAsync(x => x.ItemId == item.Id && x.ReceivedAt == null && x.ClosedAt == null, ct);
        if (note is null)
        {
            // brought back without a field report — allowed, but recorded as such
            if (item.Status is not (ItemStatus.Installed or ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.OutOfOrder or ItemStatus.Dismantled)) throw AppException.Bad($"Item is {item.Status}; nothing to receive.");
            note = new ReturnNote { Number = await numbers.NextAsync("RTN", null, ct), ItemId = item.Id, SiteId = item.SiteId, Reason = "Returned to store without field report", Kind = item.Status == ItemStatus.Installed ? "OutOfOrder" : item.Status is ItemStatus.Issued or ItemStatus.AtSite ? "Unused" : item.Status.ToString(), ReportedById = me.Id, ReportedBy = me.Name };
            db.ReturnNotes.Add(note); await db.SaveChangesAsync(ct);
            item.ReturnNoteId = note.Id;
        }
        var now = DateTime.UtcNow;
        StoreLocation loc;
        if (note.Kind == "Unused")
        {
            loc = r.StoreLocationId is null ? throw AppException.Rule("BR-005", "Choose the stock location the unused material is going back to.") : store.Locations.FirstOrDefault(l => l.Id == r.StoreLocationId && l.Kind == "Stock" && l.Active) ?? throw AppException.Rule("BR-005", "Choose an active stock location in this store.");
            item.Status = ItemStatus.Received; item.SiteId = null; item.IssueId = null; item.RequisitionId = null; item.ReturnNoteId = null;
            note.ClosedAt = now; note.Classification = ReturnClass.Pending;
            ledger.Add("Return", item, store.Id, item.Qty, me.Id, null, loc.Id, "return", note.Number, now);
            audit.Log("stock", "unused-returned", me, "item", item.Id.ToString(), next: new { note.Number, Location = loc.FullCode });
        }
        else
        {
            loc = (r.StoreLocationId is null ? store.Locations.FirstOrDefault(l => l.Kind == "Quarantine" && l.Active) : store.Locations.FirstOrDefault(l => l.Id == r.StoreLocationId && l.Kind == "Quarantine" && l.Active))
                ?? throw AppException.Rule("BR-018", "This store has no active quarantine location. Create one under Stores.");
            var expires = Map.WarrantyExpires(item);
            note.WarrantyExpires = expires; note.WarrantyEligible = expires is not null && expires >= DateOnly.FromDateTime(now);
            item.Status = ItemStatus.Returned;
            item.SiteId = null;     // DF-03: in quarantine the item is the store's again; the return note keeps the site it came from
            ledger.Add("Return", item, store.Id, item.Qty, me.Id, null, loc.Id, "return", note.Number, now);
            audit.Log("stock", "returned", me, "item", item.Id.ToString(), next: new { note.Number, Location = loc.FullCode, note.WarrantyEligible, note.WarrantyExpires });
        }
        item.StoreLocationId = loc.Id; item.ReturnedAt = now; item.UpdatedAt = now; item.UpdatedBy = me.Id;
        note.StoreId = store.Id; note.StoreLocationId = loc.Id; note.ReceivedById = me.Id; note.ReceivedBy = me.Name; note.ReceivedAt = now; note.Remarks = r.Remarks ?? note.Remarks;
        audit.Log("stock", "return-received", me, "return", note.Number, next: new { Store = store.Code, Location = loc.FullCode, note.Kind });
        await db.SaveChangesAsync(ct);
        return await GetAsync(note.Id, ct);
    }

    /// <summary>FR-RT-05/06, BR-019: repairable, warranty (only if eligible) or scrap.</summary>
    public async Task<ReturnDto> ClassifyAsync(int id, ReturnClassifyRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Return, Permissions.Procure);
        var note = await Map.Returns(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Return note");
        if (note.ReceivedAt is null) throw AppException.Bad("The item has not been received at the store yet.");
        if (note.ClosedAt is not null) throw AppException.Bad("This return is closed.");
        if (!Enum.TryParse<ReturnClass>(r.Classification, true, out var cls) || cls == ReturnClass.Pending) throw AppException.Bad("Classification must be Usable, Repairable, Warranty or Scrap.");
        var item = note.Item!;
        if (cls == ReturnClass.Usable) return await UsableAsync(me, note, r, ct);
        if (item.Status is not (ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim)) throw AppException.Bad($"Item is {item.Status}.");
        if (cls == ReturnClass.Warranty && !note.WarrantyEligible) throw AppException.Rule("BR-019", $"Warranty expired on {note.WarrantyExpires:dd-MM-yyyy}; this item is not eligible for a claim.");
        var target = cls switch { ReturnClass.Repairable => ItemStatus.UnderRepair, ReturnClass.Warranty => ItemStatus.WarrantyClaim, _ => ItemStatus.Scrap };
        if (!ItemLifecycle.Allowed(item.Status, target)) throw AppException.Rule("BR-013", $"{item.Status} → {target} is not a permitted transition.");
        if (cls == ReturnClass.Scrap)
        {
            var scrap = await db.StoreLocations.FirstOrDefaultAsync(l => l.StoreId == note.StoreId && l.Kind == "Scrap" && l.Active, ct);
            if (scrap is not null) { ledger.Add("Move", item, scrap.StoreId, 0, me.Id, item.StoreLocationId, scrap.Id, "return", note.Number); item.StoreLocationId = scrap.Id; }
        }
        note.Classification = cls; note.ClaimRef = r.ClaimRef; note.ClassifiedById = me.Id; note.ClassifiedAt = DateTime.UtcNow; note.Remarks = r.Remarks ?? note.Remarks;
        item.Status = target; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        audit.Log("stock", cls switch { ReturnClass.Repairable => "sent-for-repair", ReturnClass.Warranty => "warranty-claimed", _ => "condemned" }, me, "item", item.Id.ToString(), next: new { note.Number, note.ClaimRef }, reason: r.Remarks);
        audit.Log("stock", "return-classified", me, "return", note.Number, next: new { Classification = cls.ToString(), note.ClaimRef });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>FR-INV-033: a returned item found usable is scanned into a stock bin and is available again at once.</summary>
    async Task<ReturnDto> UsableAsync(User me, ReturnNote note, ReturnClassifyRequest r, CancellationToken ct)
    {
        var item = note.Item!;
        if (item.Status != ItemStatus.Returned) throw AppException.Rule("FR-INV-033", $"Item is {item.Status}; only an item in quarantine can be classified as usable.");
        var binCode = Codes.ParseLocation(r.StoreLocationQr);
        var loc = await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => (r.StoreLocationId != null && l.Id == r.StoreLocationId) || (binCode != null && l.FullCode == binCode), ct)
            ?? throw AppException.Rule("BR-005", "Scan or choose the stock bin the usable item goes back to.");
        if (loc.Kind != "Stock" || !loc.Active || loc.StoreId != note.StoreId) throw AppException.Rule("BR-005", $"Choose an active stock bin in {note.Store?.Code}.");
        ledger.Add("Move", item, loc.StoreId, 0, me.Id, item.StoreLocationId, loc.Id, "return", note.Number);
        item.Status = ItemStatus.Received; item.StoreLocationId = loc.Id; item.SiteId = null; item.IssueId = null; item.RequisitionId = null; item.InstallationId = null; item.InstalledAt = null; item.ReturnNoteId = null; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        note.Classification = ReturnClass.Usable; note.ClassifiedById = me.Id; note.ClassifiedAt = DateTime.UtcNow; note.ClosedAt = DateTime.UtcNow; note.Remarks = r.Remarks ?? note.Remarks;
        audit.Log("stock", "returned-usable", me, "item", item.Id.ToString(), next: new { note.Number, Location = loc.FullCode }, reason: r.Remarks);
        audit.Log("stock", "return-classified", me, "return", note.Number, next: new { Classification = "Usable", Location = loc.FullCode });
        await db.SaveChangesAsync(ct);
        return await GetAsync(note.Id, ct);
    }

    /// <summary>FR-RT-08 / BR-020: disposal approval writes the item out of stock into the scrap register.</summary>
    public async Task<ReturnDto> DisposeAsync(int id, ReturnDisposeRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Approve, ct);
        var note = await Map.Returns(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Return note");
        var item = note.Item!;
        if (item.Status != ItemStatus.Scrap) throw AppException.Rule("BR-020", $"Item is {item.Status}; only condemned (scrap) items can be disposed.");
        note.DisposalApprovedById = me.Id; note.DisposalApprovedAt = DateTime.UtcNow; note.ScrapValue = r.ScrapValue; note.ClosedAt = DateTime.UtcNow; note.Remarks = r.Remarks ?? note.Remarks;
        if (item.StoreLocationId is not null) ledger.Add("Disposal", item, note.StoreId ?? 0, -item.Qty, me.Id, item.StoreLocationId, null, "return", note.Number);
        item.Status = ItemStatus.Disposed; item.StoreLocationId = null; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        audit.Log("stock", "disposed", me, "item", item.Id.ToString(), next: new { note.Number, r.ScrapValue }, reason: r.Remarks);
        audit.Log("stock", "disposal-approved", me, "return", note.Number, next: new { r.ScrapValue });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>FR-RT-07: the repaired or warranty-replaced item comes back into issuable stock (same label, same history).</summary>
    public async Task<ReturnDto> ReinwardAsync(int id, int storeLocationId, string? remarks, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Receive, ct);
        var note = await Map.Returns(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Return note");
        var item = note.Item!;
        if (item.Status is not (ItemStatus.UnderRepair or ItemStatus.WarrantyClaim)) throw AppException.Bad($"Item is {item.Status}; only items under repair or warranty claim can be re-inwarded.");
        var loc = await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.Id == storeLocationId && l.Kind == "Stock" && l.Active, ct) ?? throw AppException.Rule("BR-005", "Choose an active stock location.");
        if (item.StoreLocationId is not null && note.StoreId is not null && loc.StoreId != note.StoreId)
        {
            ledger.Add("TransferOut", item, note.StoreId.Value, -item.Qty, me.Id, item.StoreLocationId, null, "return", note.Number);
            ledger.Add("TransferIn", item, loc.StoreId, item.Qty, me.Id, null, loc.Id, "return", note.Number);
        }
        else ledger.Add("Move", item, loc.StoreId, 0, me.Id, item.StoreLocationId, loc.Id, "return", note.Number);
        item.Status = ItemStatus.Received; item.StoreLocationId = loc.Id; item.SiteId = null; item.IssueId = null; item.RequisitionId = null; item.InstallationId = null; item.InstalledAt = null; item.ReturnNoteId = null; item.ReceivedAt = DateTime.UtcNow; item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        note.ClosedAt = DateTime.UtcNow; note.Remarks = remarks ?? note.Remarks;
        audit.Log("stock", "re-inwarded", me, "item", item.Id.ToString(), next: new { note.Number, Location = loc.FullCode, note.Classification }, reason: remarks);
        audit.Log("stock", "return-closed", me, "return", note.Number, reason: "re-inwarded to stock");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ReturnDto> GetAsync(int id, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var note = await Map.Returns(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Return note");
        return Map.Return(note);
    }

    public async Task<List<ReturnDto>> ListAsync(string? stage, int? storeId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var q = Map.Returns(db).AsNoTracking().AsQueryable();
        q = stage switch
        {
            "reported" => q.Where(x => x.ReceivedAt == null && x.ClosedAt == null),
            "quarantine" => q.Where(x => x.ReceivedAt != null && x.ClosedAt == null && x.Classification == ReturnClass.Pending),
            "repair" => q.Where(x => x.ClosedAt == null && x.Classification == ReturnClass.Repairable),
            "warranty" => q.Where(x => x.ClosedAt == null && x.Classification == ReturnClass.Warranty),
            "scrap" => q.Where(x => x.Classification == ReturnClass.Scrap),
            "closed" => q.Where(x => x.ClosedAt != null),
            _ => q,
        };
        if (storeId is not null) q = q.Where(x => x.StoreId == storeId);
        var rows = await q.OrderByDescending(x => x.Id).Take(800).ToListAsync(ct);
        return rows.Where(x => (x.Store is null || AccessPolicy.StoreVisible(me, x.Store)) || (x.Site is not null && AccessPolicy.SiteVisible(me, x.Site))).Select(Map.Return).ToList();
    }

    /// <summary>FR-RT-10 / BR-029: removed at site but not back at the store within the configured period.</summary>
    public async Task<List<ReturnDto>> OverdueAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-cfg.Controls.ReturnDays);
        var rows = await Map.Returns(db).AsNoTracking().Where(x => x.ReceivedAt == null && x.ClosedAt == null && x.ReportedAt < cutoff).OrderBy(x => x.ReportedAt).ToListAsync(ct);
        return rows.Select(Map.Return).ToList();
    }
}
