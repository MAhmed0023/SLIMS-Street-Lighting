using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Phase 3 — scan-based issue against an approved requisition; custody moves to the site location (FR-IS-01 … FR-IS-11).</summary>
public class IssueService(IAppDbContext db, AuditService audit, UserContext users, NumberingService numbers, LedgerService ledger, NotificationService notify)
{
    async Task<(Item? item, RequisitionLine? line, string? problem, string? step)> ValidateAsync(string qr, Requisition req, List<Item> already, CancellationToken ct)
    {
        if (!Codes.TryParse(qr, out var code, out var serial)) return (null, null, "Not a SLIMS label — the QR payload or its check value is invalid.", "BR-002");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == qr.Trim(), ct);
        if (item is null) return (null, null, $"Label {code} #{serial} is not registered.", "BR-002");
        if (already.Any(a => a.Id == item.Id)) return (item, null, "Already scanned in this issue.", "BR-002");
        var line = req.Lines.FirstOrDefault(l => l.ArticleId == item.ArticleId);
        if (line is null) return (item, null, $"{item.Article!.ItemCode} is not on requisition {req.Number}.", "FR-IS-03");
        if (item.Status is ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification or ItemStatus.Installed) return (item, line, $"Already issued to {item.Site?.Code ?? "a site"} ({item.Status}).", "BR-011");
        if (item.Status is ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim or ItemStatus.Scrap or ItemStatus.Disposed) return (item, line, $"Item is in quarantine/scrap ({item.Status}) and cannot be issued.", "BR-011");
        if (item.Status == ItemStatus.Reserved && item.RequisitionId != req.Id) return (item, line, "Reserved for another requisition.", "BR-009");
        if (item.Status is not (ItemStatus.Received or ItemStatus.Reserved)) return (item, line, $"Item is {item.Status}; only received stock can be issued.", "BR-011");
        if (item.StoreLocation?.StoreId != req.StoreId) return (item, line, $"Item is at store {item.StoreLocation?.Store?.Code}, not the issuing store.", "FR-IS-03");
        if (item.StoreLocation.Kind != "Stock") return (item, line, $"Item is in {item.StoreLocation.FullCode} ({item.StoreLocation.Kind}); put it away to a stock bin before issuing.", "FR-INV-010");
        if (item.Article!.Status != "Active") return (item, line, $"Article {item.Article.ItemCode} is {item.Article.Status}; it cannot be issued.", "BR-011");
        var pending = already.Where(a => a.ArticleId == item.ArticleId).Sum(a => a.Qty);
        if (line.QtyIssued + pending + item.Qty > line.QtyApproved + (item.Article!.Serialised ? 0 : item.Qty - 1)) return (item, line, $"Excess — {line.QtyApproved} {item.Article.Uom} approved, {line.QtyIssued + pending} already issued.", "BR-010");
        return (item, line, null, null);
    }

    public async Task<ScanResultDto> ScanAsync(IssueScanRequest r, string[]? alreadyScanned, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Issue, ct);
        var req = await Map.Requisitions(db).FirstOrDefaultAsync(x => x.Id == r.RequisitionId, ct) ?? throw AppException.NotFound("Requisition");
        UserContext.RequireStore(me, req.Store!);
        if (req.Status is not (RequisitionStatus.Approved or RequisitionStatus.PartIssued)) throw AppException.Rule("BR-006", $"Requisition is {req.Status}; material can only be issued against an approved requisition.");
        SiteOpen(req.Site!);
        var already = alreadyScanned is null || alreadyScanned.Length == 0 ? new List<Item>() : await Map.Items(db).Where(i => alreadyScanned.Contains(i.QrCode)).ToListAsync(ct);
        var (item, _, problem, step) = await ValidateAsync(r.Qr, req, already, ct);
        if (problem is not null) { audit.Log("stock", "issue-scan-rejected", me, "item", item?.Id.ToString(), reason: problem, next: new { req.Number }); await db.SaveChangesAsync(ct); }
        return new ScanResultDto(problem is null, problem ?? $"OK — {item!.Article!.ItemCode} #{item.Serial} for {req.Site!.Code}.", item is null ? null : Map.Item(item), step);
    }

    public async Task<IssueDto> PostAsync(IssuePostRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Issue, ct);
        var req = await Map.Requisitions(db).FirstOrDefaultAsync(x => x.Id == r.RequisitionId, ct) ?? throw AppException.NotFound("Requisition");
        UserContext.RequireStore(me, req.Store!);
        if (req.Status is not (RequisitionStatus.Approved or RequisitionStatus.PartIssued)) throw AppException.Rule("BR-006", $"Requisition is {req.Status}; material can only be issued against an approved requisition.");
        SiteOpen(req.Site!);
        if (r.Lines is { Count: > 0 }) r = r with { QrCodes = [.. r.QrCodes ?? [], .. await PickAsync(req, r.Lines, ct)], Lines = null };
        var qrs = (r.QrCodes ?? []).Select(q => q.Trim()).Where(q => q != "").Distinct().ToArray();
        if (qrs.Length == 0) throw AppException.Rule("BR-001", "Enter the quantity to issue for at least one item.");
        var items = new List<Item>(); var lines = new Dictionary<Guid, RequisitionLine>();
        foreach (var qr in qrs)
        {
            var (item, line, problem, step) = await ValidateAsync(qr, req, items, ct);
            if (problem is not null) throw AppException.Rule(step ?? "FR-IS-03", $"{qr}: {problem}");
            items.Add(item!); lines[item!.Id] = line!;
        }
        var issue = new Issue { Number = await numbers.NextAsync("ISS", req.Store!.Code, ct), GatePassNo = await numbers.NextAsync("GP", req.Store.Code, ct), RequisitionId = req.Id, StoreId = req.StoreId, SiteId = req.SiteId,
            IssuedById = me.Id, IssuedBy = me.Name, IssuedTo = r.IssuedTo, Vehicle = r.Vehicle, Remarks = r.Remarks, ItemCount = items.Count, Qty = items.Sum(i => i.Qty) };
        db.Issues.Add(issue);
        await db.SaveChangesAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var i in items)
        {
            var from = i.StoreLocation?.FullCode;
            ledger.Add("Issue", i, req.StoreId, -i.Qty, me.Id, i.StoreLocationId, null, "issue", issue.Number, now);
            i.Status = ItemStatus.Issued; i.SiteId = req.SiteId; i.IssueId = issue.Id; i.RequisitionId = req.Id; i.StoreLocationId = null; i.IssuedAt = now; i.UpdatedAt = now; i.UpdatedBy = me.Id;
            lines[i.Id].QtyIssued += i.Qty;
            audit.Log("stock", "issued", me, "item", i.Id.ToString(), next: new { Issue = issue.Number, issue.GatePassNo, From = from, To = req.Site!.Code, Req = req.Number, i.Qty });
        }
        // BR-009 / FR-RQ-10: surplus reservations on fully-issued lines go back to stock.
        // The status filter is applied in memory: the items issued above are still Reserved in the
        // database until SaveChanges runs, so a server-side filter would sweep them up too.
        var justIssued = items.Select(i => i.Id).ToHashSet();
        foreach (var l in req.Lines.Where(l => l.QtyIssued >= l.QtyApproved))
        {
            var surplus = (await db.Items.Where(i => i.RequisitionId == req.Id && i.ArticleId == l.ArticleId).ToListAsync(ct))
                .Where(i => !justIssued.Contains(i.Id) && i.Status == ItemStatus.Reserved).ToList();
            foreach (var s in surplus) { s.Status = ItemStatus.Received; s.RequisitionId = null; s.UpdatedAt = now; s.UpdatedBy = me.Id; audit.Log("stock", "reservation-released", me, "item", s.Id.ToString(), reason: "line fully issued"); }
        }
        req.Status = req.Lines.All(l => l.QtyIssued >= l.QtyApproved) ? RequisitionStatus.Issued : RequisitionStatus.PartIssued;
        audit.Log("stock", "issue-posted", me, "issue", issue.Number, next: new { req.Number, Site = req.Site!.Code, issue.GatePassNo, issue.ItemCount, issue.Qty });
        await notify.NotifyAsync([.. await notify.SiteRoleAsync(req.Site!, "supervisor", ct), .. await notify.SiteRoleAsync(req.Site!, "officer", ct)], "NTF-05", $"Material issued to {req.Site!.Code}",
            $"Issue {issue.Number}, gate pass {issue.GatePassNo}: {issue.ItemCount} label(s) from {req.Store!.Code}{(r.IssuedTo is null ? "" : ", collected by " + r.IssuedTo)}. Acknowledge receipt at site.", "/field", "issue", issue.Number, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(issue.Id, ct);
    }

    /// <summary>Stock a requisition can be issued from, per article: reserved for it, free in stock bins, and waiting in the Receiving area.</summary>
    public async Task<List<IssueAvailableDto>> AvailableAsync(int requisitionId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Issue, ct);
        var req = await Map.Requisitions(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == requisitionId, ct) ?? throw AppException.NotFound("Requisition");
        UserContext.RequireStore(me, req.Store!);
        var ids = req.Lines.Select(l => l.ArticleId).ToList();
        var rows = await db.Items.AsNoTracking().Where(i => ids.Contains(i.ArticleId) && i.StoreLocation!.StoreId == req.StoreId
                && (i.Status == ItemStatus.Received || (i.Status == ItemStatus.Reserved && i.RequisitionId == req.Id)))
            .Select(i => new { i.ArticleId, i.Qty, i.Status, i.StoreLocation!.Kind }).ToListAsync(ct);
        return req.Lines.Select(l => new IssueAvailableDto(l.ArticleId, l.Article?.ItemCode ?? "", l.Article?.Uom ?? "", l.Article?.PackSize ?? 1,
            rows.Where(x => x.ArticleId == l.ArticleId && x.Status == ItemStatus.Reserved && x.Kind == "Stock").Sum(x => x.Qty),
            rows.Where(x => x.ArticleId == l.ArticleId && x.Status == ItemStatus.Received && x.Kind == "Stock").Sum(x => x.Qty),
            rows.Where(x => x.ArticleId == l.ArticleId && x.Kind == "Receiving").Sum(x => x.Qty))).ToList();
    }

    /// <summary>Issue by quantity: picks the labels for each line — this requisition's reservation first, then the oldest receipt —
    /// from the store's stock bins, so the posting applies exactly the same checks as a scanned issue.</summary>
    async Task<List<string>> PickAsync(Requisition req, List<IssueLineQty> lines, CancellationToken ct)
    {
        var picked = new List<string>();
        foreach (var l in lines.Where(l => l.Qty != 0))
        {
            if (l.Qty < 0) throw AppException.Bad("Quantities cannot be negative.");
            var line = req.Lines.FirstOrDefault(x => x.ArticleId == l.ArticleId) ?? throw AppException.Rule("FR-IS-03", "That item is not on the requisition.");
            var a = line.Article ?? await db.Articles.AsNoTracking().FirstAsync(x => x.Id == l.ArticleId, ct);
            var balance = line.QtyApproved - line.QtyIssued;
            if (l.Qty > balance) throw AppException.Rule("BR-010", $"{a.ItemCode}: {balance:0.###} {a.Uom} is still to be issued on this requisition; {l.Qty:0.###} entered.");
            var stock = await db.Items.AsNoTracking().Where(i => i.ArticleId == l.ArticleId && i.StoreLocation!.StoreId == req.StoreId && i.StoreLocation.Kind == "Stock"
                    && (i.Status == ItemStatus.Received || (i.Status == ItemStatus.Reserved && i.RequisitionId == req.Id)))
                .OrderBy(i => i.Status == ItemStatus.Reserved ? 0 : 1).ThenBy(i => i.ReceivedAt).ThenBy(i => i.Serial)
                .Select(i => new { i.QrCode, i.Qty }).ToListAsync(ct);
            decimal sum = 0; int k = 0;
            while (sum < l.Qty && k < stock.Count) { sum += stock[k].Qty; picked.Add(stock[k].QrCode); k++; }
            if (sum < l.Qty)
            {
                var waiting = await db.Items.Where(i => i.ArticleId == l.ArticleId && i.Status == ItemStatus.Received && i.StoreLocation!.StoreId == req.StoreId && i.StoreLocation.Kind == "Receiving").SumAsync(i => i.Qty, ct);
                throw AppException.Rule("BR-008", $"{a.ItemCode}: only {sum:0.###} {a.Uom} is in the stock bins of {req.Store!.Code}; {l.Qty:0.###} entered." + (waiting > 0 ? $" {waiting:0.###} more is still in the Receiving area." : ""));
            }
            // a bulk pack is never split: the last pack goes whole, as with a scanned issue (the posting allows up to one pack over)
        }
        return picked;
    }

    /// <summary>DF-05: a site put On hold or Completed after approval must not receive material.</summary>
    static void SiteOpen(Site s) { if (s.Status is "On hold" or "Completed") throw AppException.Rule("BR-007", $"Site {s.Code} is {s.Status}; material cannot be issued to it."); }

    public async Task<IssueDto> GetAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var x = await Map.Issues(db).AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Issue");
        if (!(AccessPolicy.StoreVisible(me, x.Store!) || AccessPolicy.SiteVisible(me, x.Site!))) throw AppException.Forbidden("This issue is outside your scope.", "scope");
        var items = await Map.Items(db).AsNoTracking().Where(i => i.IssueId == id).OrderBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).ToListAsync(ct);
        return Map.Issue(x, items);
    }

    public async Task<List<IssueDto>> ListAsync(int? storeId, int? siteId, bool? unacknowledged, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var q = Map.Issues(db).AsNoTracking().AsQueryable();
        if (storeId is not null) q = q.Where(i => i.StoreId == storeId);
        if (siteId is not null) q = q.Where(i => i.SiteId == siteId);
        if (unacknowledged == true) q = q.Where(i => i.ReceivedAt == null);
        var rows = await q.OrderByDescending(i => i.Id).Take(500).ToListAsync(ct);
        return rows.Where(x => AccessPolicy.StoreVisible(me, x.Store!) || AccessPolicy.SiteVisible(me, x.Site!)).Select(x => Map.Issue(x, new())).ToList();
    }

    /// <summary>FR-IS-09: the Lighting Officer confirms the material reached the site.</summary>
    public async Task<IssueDto> AcknowledgeAsync(int id, AckRequest a, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Install, Permissions.Requisition);
        var x = await Map.Issues(db).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Issue");
        UserContext.RequireSite(me, x.Site!);
        if (x.ReceivedAt is not null) throw AppException.Bad("Already acknowledged.");
        x.ReceivedById = me.Id; x.ReceivedAt = DateTime.UtcNow; x.ReceivedLat = a.Lat; x.ReceivedLng = a.Lng;
        var items = await db.Items.Where(i => i.IssueId == id && i.Status == ItemStatus.Issued).ToListAsync(ct);
        foreach (var i in items) { i.Status = ItemStatus.AtSite; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id; audit.Log("stock", "at-site", me, "item", i.Id.ToString(), next: new { x.Number, a.Lat, a.Lng, a.AccuracyM }); }
        audit.Log("stock", "issue-acknowledged", me, "issue", x.Number, next: new { a.Lat, a.Lng, a.AccuracyM, Items = items.Count });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Items in the field for the sites this user can see — the officer's work list.</summary>
    public async Task<List<ItemDto>> FieldItemsAsync(int? siteId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var q = Map.Items(db).AsNoTracking().Where(i => i.SiteId != null && (i.Status == ItemStatus.Issued || i.Status == ItemStatus.AtSite || i.Status == ItemStatus.PendingVerification));
        if (siteId is not null) q = q.Where(i => i.SiteId == siteId);
        var rows = await q.OrderBy(i => i.Site!.Code).ThenBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).ToListAsync(ct);
        return rows.Where(i => AccessPolicy.SiteVisible(me, i.Site!)).Select(Map.Item).ToList();
    }
}
