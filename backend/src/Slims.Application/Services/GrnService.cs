using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Phase 1 — goods receipt by QR scan against an open purchase order (FR-GR-01 … FR-GR-14).</summary>
public class GrnService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, NumberingService numbers, LedgerService ledger, StoreControlService control, IFileStorage files, ICurrentUser current)
{
    async Task<(Item? item, string? problem, string? step)> ValidateAsync(string qr, int poId, double tolerancePct, CancellationToken ct)
    {
        if (!Codes.TryParse(qr, out var code, out var serial)) return (null, "Not a SLIMS label — the QR payload or its check value is invalid.", "BR-002");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == qr.Trim(), ct);
        if (item is null) return (null, $"Label {code} #{serial} is not registered in the system.", "BR-002");
        if (item.Article!.PurchaseOrderId != poId) return (item, $"This item belongs to PO {item.Article.PurchaseOrder?.Number}, not the PO being received.", "FR-GR-03");
        if (item.Status != ItemStatus.Created) return (item, $"Already {item.Status} — a label can only be received once.", "BR-002");
        var line = await db.PurchaseOrderLines.AsNoTracking().FirstAsync(l => l.Id == item.Article.PurchaseOrderLineId, ct);
        var received = await db.Items.Where(i => i.ArticleId == item.ArticleId && i.Status != ItemStatus.Created && i.Status != ItemStatus.Rejected && i.Status != ItemStatus.Cancelled).SumAsync(i => i.Qty, ct);
        if (received + item.Qty > line.Qty * (1 + (decimal)tolerancePct / 100)) return (item, $"Receipt would exceed the PO line quantity ({received}+{item.Qty} > {line.Qty}).", "BR-014");
        return (item, null, null);
    }

    public async Task<ScanResultDto> ScanAsync(GrnScanRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Receive, ct);
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.StoreId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, store);
        var cfg = await configs.GetAsync(ct);
        var (item, problem, step) = await ValidateAsync(r.Qr, r.PurchaseOrderId, cfg.Controls.ReceiptTolerancePct, ct);
        if (problem is not null) { audit.Log("stock", "grn-scan-rejected", me, "item", item?.Id.ToString(), reason: problem); await db.SaveChangesAsync(ct); }
        return new ScanResultDto(problem is null, problem ?? $"OK — {item!.Article!.ItemCode} #{item.Serial} matches PO {item.Article.PurchaseOrder?.Number}.", item is null ? null : Map.Item(item), step);
    }

    /// <summary>FR-INV-005/010: accepted labels go into the store's Receiving area (not yet available); rejected labels are recorded
    /// with a reason code and optional photograph, never enter stock, and raise a discrepancy note for Procurement.</summary>
    public async Task<GrnDto> PostAsync(GrnPostRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Receive, ct);
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.StoreId, ct) ?? throw AppException.NotFound("Store");
        UserContext.RequireStore(me, store);
        StoreLocation loc = await control.ReceivingLocationAsync(store.Id, ct);
        var po = await db.PurchaseOrders.Include(p => p.Vendor).FirstOrDefaultAsync(p => p.Id == r.PurchaseOrderId, ct) ?? throw AppException.NotFound("Purchase order");
        if (po.Status != "Open") throw AppException.Bad("The purchase order is closed.");
        var cfg = await configs.GetAsync(ct);
        if (r.StoreLocationId is int locId)
        {
            loc = await db.StoreLocations.Include(l => l.Store).FirstOrDefaultAsync(l => l.Id == locId && l.StoreId == store.Id && l.Active, ct) ?? throw AppException.NotFound("Store location");
            if (loc.Kind is not ("Stock" or "Receiving")) throw AppException.Rule("BR-005", $"{loc.FullCode} is a {loc.Kind} location; receive into a stock bin or the Receiving area.");
        }
        if (r.Lines is { Count: > 0 }) r = await ByQuantityAsync(r, po.Id, ct);
        var rejects = (r.Rejections ?? new()).Where(x => !string.IsNullOrWhiteSpace(x.Qr)).GroupBy(x => x.Qr.Trim()).Select(g => g.First() with { Qr = g.Key }).ToList();
        var qrs = (r.QrCodes ?? []).Select(q => q.Trim()).Where(q => q != "" && rejects.All(x => x.Qr != q)).Distinct().ToArray();
        if (qrs.Length == 0 && rejects.Count == 0) throw AppException.Rule("BR-001", "Enter the quantity received for at least one item.");
        if (!string.IsNullOrWhiteSpace(r.Condition) && !cfg.ReceiptConditions.Contains(r.Condition)) throw AppException.Bad($"Unknown condition \"{r.Condition}\".");
        var items = new List<Item>();
        foreach (var qr in qrs)
        {
            var (item, problem, step) = await ValidateAsync(qr, po.Id, cfg.Controls.ReceiptTolerancePct, ct);
            if (problem is not null) throw AppException.Rule(step ?? "FR-GR-03", $"{qr}: {problem}");
            items.Add(item!);
        }
        var rejected = new List<(Item Item, GrnReject R)>();
        foreach (var x in rejects)
        {
            if (string.IsNullOrWhiteSpace(x.ReasonCode) || !cfg.RejectReasons.Contains(x.ReasonCode)) throw AppException.Bad($"{x.Qr}: choose a rejection reason code from the list.");
            if (!Codes.TryParse(x.Qr, out _, out _)) throw AppException.Rule("BR-002", $"{x.Qr}: not a SLIMS label.");
            var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == x.Qr, ct) ?? throw AppException.NotFound("Item " + x.Qr);
            if (item.Article!.PurchaseOrderId != po.Id) throw AppException.Rule("FR-GR-03", $"{x.Qr}: belongs to PO {item.Article.PurchaseOrder?.Number}.");
            if (item.Status != ItemStatus.Created) throw AppException.Rule("BR-002", $"{x.Qr}: already {item.Status}.");
            rejected.Add((item, x));
        }
        var grn = new Grn { Number = await numbers.NextAsync("GRN", store.Code, ct), PurchaseOrderId = po.Id, StoreId = store.Id, ChallanNo = r.ChallanNo, InvoiceNo = r.InvoiceNo, InvoiceDate = r.InvoiceDate, PostedById = me.Id, PostedBy = me.Name, Remarks = r.Remarks,
            Condition = string.IsNullOrWhiteSpace(r.Condition) ? null : r.Condition, RejectedCount = rejected.Count, ItemCount = items.Count, Qty = items.Sum(i => i.Qty), Value = items.Sum(i => i.Qty * i.Article!.Rate) };
        db.Grns.Add(grn);
        await db.SaveChangesAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var i in items)
        {
            i.Status = ItemStatus.Received; i.StoreLocationId = loc.Id; i.GrnId = grn.Id; i.ReceivedAt = now; i.UpdatedAt = now; i.UpdatedBy = me.Id;
            ledger.Add("Receipt", i, store.Id, i.Qty, me.Id, null, loc.Id, "grn", grn.Number, now);
            audit.Log("stock", "received", me, "item", i.Id.ToString(), next: new { Grn = grn.Number, Location = loc.FullCode, Po = po.Number, i.Qty, grn.Condition }, reason: r.ChallanNo is null ? null : "Challan " + r.ChallanNo);
        }
        foreach (var (i, x) in rejected)
        {
            Guid? photo = null;
            if (!string.IsNullOrWhiteSpace(x.PhotoBase64))
            {
                var bytes = Convert.FromBase64String(x.PhotoBase64.Contains(',') ? x.PhotoBase64[(x.PhotoBase64.IndexOf(',') + 1)..] : x.PhotoBase64);
                if (bytes.Length > cfg.Controls.EvidenceMaxMB * 1024L * 1024) throw AppException.Bad($"{x.Qr}: photograph exceeds {cfg.Controls.EvidenceMaxMB} MB.");
                var path = await files.SaveAsync(Path.Combine("receipts", grn.Number.Replace('/', '-')), $"reject_{i.Serial}.jpg", new MemoryStream(bytes), ct);
                var ev = new Evidence { Id = Guid.NewGuid(), Kind = "reject", Name = $"reject_{i.Serial}.jpg", StoragePath = path, Size = bytes.Length, CapturedAt = now, UploadedById = me.Id, UploadedBy = me.Name, Ip = current.Ip, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() };
                db.Evidence.Add(ev); photo = ev.Id;
            }
            i.Status = ItemStatus.Rejected; i.GrnId = grn.Id; i.UpdatedAt = now; i.UpdatedBy = me.Id;
            audit.Log("stock", "rejected-at-receipt", me, "item", i.Id.ToString(), next: new { Grn = grn.Number, Po = po.Number, x.ReasonCode, Photo = photo }, reason: x.Remarks ?? x.ReasonCode);
        }
        foreach (var g in rejected.GroupBy(x => x.Item.ArticleId))
        {
            var d = new DiscrepancyNote { Number = await numbers.NextAsync("DSC", null, ct), PurchaseOrderId = po.Id, ArticleId = g.Key, GrnId = grn.Id, Kind = "Rejected", Qty = g.Sum(x => x.Item.Qty), RaisedById = me.Id,
                Reason = Trunc($"Rejected at receipt ({grn.Number}): " + string.Join("; ", g.GroupBy(x => x.R.ReasonCode).Select(r2 => $"{r2.Count()} × {r2.Key}")), 400) };
            db.DiscrepancyNotes.Add(d);
            audit.Log("stock", "discrepancy-raised", me, "discrepancy", d.Number, next: new { po.Number, d.Kind, d.Qty, d.Reason });
        }
        audit.Log("stock", "grn-posted", me, "grn", grn.Number, next: new { po.Number, Store = store.Code, Location = loc.FullCode, grn.ItemCount, grn.Qty, grn.Value, grn.Condition, Rejected = rejected.Count });
        await db.SaveChangesAsync(ct);
        grn.PurchaseOrder = po; grn.Store = store;
        return Map.Grn(grn);
    }
    /// <summary>Receipt by quantity: turns "N received, M rejected" per article into the article's next unreceived labels, in serial order,
    /// so the posting below applies exactly the same checks as a scanned receipt.</summary>
    async Task<GrnPostRequest> ByQuantityAsync(GrnPostRequest r, int poId, CancellationToken ct)
    {
        var qrs = new List<string>(r.QrCodes ?? []); var rejects = new List<GrnReject>(r.Rejections ?? new());
        foreach (var l in r.Lines!.Where(l => l.Qty > 0 || l.RejectedQty > 0))
        {
            if (l.Qty < 0 || l.RejectedQty < 0) throw AppException.Bad("Quantities cannot be negative.");
            var a = await db.Articles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == l.ArticleId, ct) ?? throw AppException.NotFound("Article");
            if (a.PurchaseOrderId != poId) throw AppException.Rule("FR-GR-03", $"{a.ItemCode} is not on this purchase order.");
            var open = await db.Items.AsNoTracking().Where(i => i.ArticleId == a.Id && i.Status == ItemStatus.Created).OrderBy(i => i.Serial).Select(i => new { i.QrCode, i.Qty }).ToListAsync(ct);
            var need = l.Qty + l.RejectedQty; var have = open.Sum(i => i.Qty);
            if (need > have) throw AppException.Rule("BR-014", $"{a.ItemCode}: only {have:0.###} {a.Uom} is still due on this PO line; {need:0.###} entered.");
            int k = 0;
            List<string> Take(decimal q, string what)
            {
                var got = new List<string>(); decimal sum = 0;
                while (sum < q) { sum += open[k].Qty; got.Add(open[k].QrCode); k++; }
                if (sum != q) throw AppException.Bad($"{a.ItemCode}: {what} must be in whole packs of {a.PackSize:0.###} {a.Uom}.");
                return got;
            }
            qrs.AddRange(Take(l.Qty, "the quantity received"));
            if (l.RejectedQty > 0)
            {
                if (string.IsNullOrWhiteSpace(l.RejectReason)) throw AppException.Bad($"{a.ItemCode}: choose the reason for the rejected quantity.");
                var bad = Take(l.RejectedQty, "the rejected quantity");
                rejects.AddRange(bad.Select((q, n) => new GrnReject(q, l.RejectReason!, l.RejectRemarks, n == 0 ? l.RejectPhotoBase64 : null)));
            }
        }
        if (qrs.Count == 0 && rejects.Count == 0) throw AppException.Bad("Enter the quantity received for at least one item.");
        return r with { QrCodes = [.. qrs], Rejections = rejects, Lines = null };
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    public async Task<List<GrnDto>> ListAsync(int? storeId, int? poId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var rows = await db.Grns.AsNoTracking().Include(g => g.PurchaseOrder).ThenInclude(p => p!.Vendor).Include(g => g.Store).Where(g => (storeId == null || g.StoreId == storeId) && (poId == null || g.PurchaseOrderId == poId)).OrderByDescending(g => g.Id).Take(500).ToListAsync(ct);
        return rows.Where(g => AccessPolicy.StoreVisible(me, g.Store!)).Select(Map.Grn).ToList();
    }

    public async Task<List<ItemDto>> GrnItemsAsync(int grnId, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return (await Map.Items(db).AsNoTracking().Where(i => i.GrnId == grnId).OrderBy(i => i.Serial).ToListAsync(ct)).Select(Map.Item).ToList();
    }

    /// <summary>FR-GR-13: reversal is only possible while nothing from the GRN has moved on.</summary>
    public async Task ReverseAsync(int grnId, string reason, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Approve, ct);
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Bad("A reason is required to reverse a GRN.");
        var grn = await db.Grns.FirstOrDefaultAsync(g => g.Id == grnId, ct) ?? throw AppException.NotFound("GRN");
        if (grn.Reversed) throw AppException.Bad("Already reversed.");
        var items = await db.Items.Where(i => i.GrnId == grnId).ToListAsync(ct);
        if (items.Any(i => i.Status is not (ItemStatus.Received or ItemStatus.Rejected))) throw AppException.Rule("FR-GR-13", "Some items from this GRN have already been reserved, issued or moved; it can no longer be reversed.");
        foreach (var i in items)
        {
            if (i.Status == ItemStatus.Received) ledger.Add("ReceiptReversal", i, grn.StoreId, -i.Qty, me.Id, i.StoreLocationId, null, "grn", grn.Number);
            i.Status = ItemStatus.Created; i.StoreLocationId = null; i.GrnId = null; i.ReceivedAt = null; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id; audit.Log("stock", "receipt-reversed", me, "item", i.Id.ToString(), reason: reason);
        }
        grn.Reversed = true;
        audit.Log("stock", "grn-reversed", me, "grn", grn.Number, reason: reason);
        await db.SaveChangesAsync(ct);
    }

    // ---- discrepancy / short-receipt notes -----------------------------------------------------
    public async Task<DiscrepancyDto> RaiseDiscrepancyAsync(DiscrepancyRequest r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Receive, Permissions.Procure);
        var po = await db.PurchaseOrders.Include(p => p.Vendor).FirstOrDefaultAsync(p => p.Id == r.PurchaseOrderId, ct) ?? throw AppException.NotFound("Purchase order");
        if (r.Kind is not ("Short" or "Excess" or "Rejected")) throw AppException.Bad("Kind must be Short, Excess or Rejected.");
        if (r.Qty <= 0 || string.IsNullOrWhiteSpace(r.Reason)) throw AppException.Bad("Quantity and reason are required.");
        var d = new DiscrepancyNote { Number = await numbers.NextAsync("DSC", null, ct), PurchaseOrderId = po.Id, ArticleId = r.ArticleId, GrnId = r.GrnId, Kind = r.Kind, Qty = r.Qty, Reason = r.Reason.Trim(), RaisedById = me.Id };
        db.DiscrepancyNotes.Add(d);
        audit.Log("stock", "discrepancy-raised", me, "discrepancy", d.Number, next: new { po.Number, d.Kind, d.Qty, d.Reason });
        await db.SaveChangesAsync(ct);
        d.PurchaseOrder = po; d.Article = r.ArticleId is null ? null : await db.Articles.FindAsync([r.ArticleId], ct);
        return Map.Discrepancy(d);
    }

    public async Task<List<DiscrepancyDto>> DiscrepanciesAsync(string? status, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return (await db.DiscrepancyNotes.AsNoTracking().Include(d => d.PurchaseOrder).ThenInclude(p => p!.Vendor).Include(d => d.Article).Where(d => string.IsNullOrEmpty(status) || d.Status == status).OrderByDescending(d => d.Id).ToListAsync(ct)).Select(Map.Discrepancy).ToList();
    }

    public async Task ResolveDiscrepancyAsync(int id, string resolution, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Procure, Permissions.Approve);
        var d = await db.DiscrepancyNotes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Discrepancy note");
        if (string.IsNullOrWhiteSpace(resolution)) throw AppException.Bad("Say how it was resolved.");
        d.Status = "Resolved"; d.Resolution = resolution.Trim(); d.ResolvedAt = DateTime.UtcNow;
        audit.Log("stock", "discrepancy-resolved", me, "discrepancy", d.Number, reason: d.Resolution);
        await db.SaveChangesAsync(ct);
    }
}
