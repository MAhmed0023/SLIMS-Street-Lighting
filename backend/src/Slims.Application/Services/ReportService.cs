using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

public record ReportFilters(DateTime? From = null, DateTime? To = null, string? Zone = null, int? StoreId = null, int? SiteId = null, int? PoId = null, int? VendorId = null);

/// <summary>Section 14 — every report is built from the transaction record, never compiled by hand (FR-RP-01).</summary>
public class ReportService(IAppDbContext db, UserContext users)
{
    public static readonly (string Key, string Title, string Audience)[] Catalogue =
    [
        ("grn", "Goods Receipt / Inward Register", "Store · Procurement · Audit"),
        ("discrepancies", "Discrepancy & Short-Receipt Report", "Procurement"),
        ("requisitions", "Requisition Status Report", "Supervisor · Store"),
        ("indents", "Auto Purchase Indents (shortfall at requisition)", "Procurement"),
        ("issues", "Issue Register & Gate Passes", "Store · Audit"),
        ("installations", "Installation Activity Report", "PD Office · Supervisor"),
        ("exceptions", "Geo-Fence Exception Report", "Supervisor · Audit"),
        ("consumption-site", "Site-wise Consumption", "Supervisor · Finance"),
        ("consumption-po", "PO-wise Consumption & Balance", "Procurement · Finance"),
        ("returns", "Out-of-Order & Return Report", "Store · Supervisor"),
        ("failures", "Failure Analysis by Article / Vendor / PO", "Procurement · PD Office"),
        ("warranty", "Warranty Claim Register", "Procurement · Finance"),
        ("scrap", "Scrap & Condemnation Register", "Store · Audit · Finance"),
        ("ageing", "Stock Ageing", "Store In-Charge"),
        ("officers", "Officer Productivity", "Supervisor · PD Office"),
        ("reconciliation", "Stock Reconciliation (RPT-04)", "Store In-Charge · Audit"),
        ("adjustments", "Stock Adjustment Register", "Store In-Charge · Audit"),
        ("transfers", "Inter-store Transfer Register", "Store · Audit"),
        ("evidence", "Evidence Register (RPT-09)", "PD Office · Audit"),
    ];

    public static string ToCsv(ReportDto r)
    {
        static string Cell(object? v) => v switch
        {
            null => "",
            DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc).AddHours(5).ToString("yyyy-MM-dd HH:mm"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            _ => Quote(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? ""),
        };
        static string Quote(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Quote(r.Title));
        if (r.Filters.Count > 0) sb.AppendLine(Quote(string.Join("; ", r.Filters.Select(f => f.Key + ": " + f.Value))));
        sb.AppendLine(string.Join(',', r.Columns.Select(Quote)));
        foreach (var row in r.Rows) sb.AppendLine(string.Join(',', row.Select(Cell)));
        return sb.ToString();
    }

    public async Task<ReportDto> RunAsync(string key, ReportFilters f, CancellationToken ct) => await RunForAsync(await users.RequireAsync(Permissions.View, ct), key, f, ct);

    /// <summary>Runs a report within a given user's scope — the caller's, or a schedule owner's for automatic delivery.</summary>
    public async Task<ReportDto> RunForAsync(User me, string key, ReportFilters f, CancellationToken ct)
    {
        var title = Catalogue.FirstOrDefault(c => c.Key == key).Title ?? throw AppException.NotFound("Report");
        var filters = new Dictionary<string, string>();
        if (f.From is not null) filters["from"] = f.From.Value.ToString("dd-MM-yyyy"); if (f.To is not null) filters["to"] = f.To.Value.ToString("dd-MM-yyyy");
        if (f.Zone is not null) filters["zone"] = f.Zone; if (f.StoreId is not null) filters["store"] = f.StoreId.ToString()!; if (f.SiteId is not null) filters["site"] = f.SiteId.ToString()!;
        var from = f.From ?? DateTime.MinValue; var to = (f.To ?? DateTime.MaxValue.AddDays(-1)).Date.AddDays(1);
        var sites = (await db.Sites.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.SiteVisible(me, s) && (f.Zone is null || s.Zone == f.Zone) && (f.SiteId is null || s.Id == f.SiteId)).ToDictionary(s => s.Id);
        var stores = (await db.Stores.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.StoreVisible(me, s) && (f.StoreId is null || s.Id == f.StoreId)).ToDictionary(s => s.Id);
        string[] cols; List<object?[]> rows;
        switch (key)
        {
            case "grn":
                {
                    var g = await db.Grns.AsNoTracking().Include(x => x.PurchaseOrder).ThenInclude(p => p!.Vendor).Include(x => x.Store).Where(x => x.PostedAt >= from && x.PostedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["GRN No", "Date", "Store", "PO", "Vendor", "Challan", "Invoice", "Labels", "Qty", "Value", "Posted by", "Reversed"];
                    rows = g.Where(x => stores.ContainsKey(x.StoreId)).Select(x => new object?[] { x.Number, x.PostedAt, x.Store!.Code, x.PurchaseOrder!.Number, x.PurchaseOrder.Vendor!.Name, x.ChallanNo, x.InvoiceNo, x.ItemCount, x.Qty, x.Value, x.PostedBy, x.Reversed ? "Yes" : "" }).ToList();
                    break;
                }
            case "discrepancies":
                {
                    var d = await db.DiscrepancyNotes.AsNoTracking().Include(x => x.PurchaseOrder).ThenInclude(p => p!.Vendor).Include(x => x.Article).Where(x => x.RaisedAt >= from && x.RaisedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Note No", "Raised", "PO", "Vendor", "Item code", "Kind", "Qty", "Reason", "Status", "Resolution", "Age (d)"];
                    rows = d.Select(x => new object?[] { x.Number, x.RaisedAt, x.PurchaseOrder!.Number, x.PurchaseOrder.Vendor!.Name, x.Article?.ItemCode, x.Kind, x.Qty, x.Reason, x.Status, x.Resolution, (int)((x.ResolvedAt ?? DateTime.UtcNow) - x.RaisedAt).TotalDays }).ToList();
                    break;
                }
            case "requisitions":
                {
                    var r = await Map.Requisitions(db).AsNoTracking().Where(x => x.RaisedAt >= from && x.RaisedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Req No", "Raised", "Site", "Zone", "Store", "Raised by", "Status", "Lines", "Requested", "Approved", "Issued", "Shortfall", "Decided by", "Turnaround (h)"];
                    rows = r.Where(x => sites.ContainsKey(x.SiteId) || stores.ContainsKey(x.StoreId)).Select(x => new object?[] { x.Number, x.RaisedAt, x.Site!.Code, x.Site.Zone, x.Store!.Code, x.RaisedBy, x.Status.ToString(), x.Lines.Count, x.Lines.Sum(l => l.QtyRequested), x.Lines.Sum(l => l.QtyApproved), x.Lines.Sum(l => l.QtyIssued), x.Lines.Sum(l => l.Shortfall), x.ApprovedBy, x.DecidedAt is null ? null : Math.Round((x.DecidedAt.Value - x.RaisedAt).TotalHours, 1) }).ToList();
                    break;
                }
            case "indents":
                {
                    var r = await Map.Requisitions(db).AsNoTracking().Where(x => x.Lines.Any(l => l.IndentRaised) && x.RaisedAt >= from && x.RaisedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Req No", "Raised", "Store", "Site", "Item code", "Description", "Requested", "Shortfall", "PO", "Vendor"];
                    rows = r.SelectMany(x => x.Lines.Where(l => l.IndentRaised).Select(l => new object?[] { x.Number, x.RaisedAt, x.Store!.Code, x.Site!.Code, l.Article!.ItemCode, l.Article.Description, l.QtyRequested, l.Shortfall, null, null })).ToList();
                    break;
                }
            case "issues":
                {
                    var i = await Map.Issues(db).AsNoTracking().Where(x => x.IssuedAt >= from && x.IssuedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Issue No", "Gate pass", "Date", "Store", "Site", "Requisition", "Issued by", "Issued to", "Labels", "Qty", "Acknowledged at site", "Ack delay (h)"];
                    rows = i.Where(x => stores.ContainsKey(x.StoreId) || sites.ContainsKey(x.SiteId)).Select(x => new object?[] { x.Number, x.GatePassNo, x.IssuedAt, x.Store!.Code, x.Site!.Code, x.Requisition!.Number, x.IssuedBy, x.IssuedTo, x.ItemCount, x.Qty, x.ReceivedAt, x.ReceivedAt is null ? null : Math.Round((x.ReceivedAt.Value - x.IssuedAt).TotalHours, 1) }).ToList();
                    break;
                }
            case "installations":
            case "exceptions":
                {
                    var q = Map.Installations(db).AsNoTracking().Where(x => x.SubmittedAt >= from && x.SubmittedAt < to);
                    if (key == "exceptions") q = q.Where(x => x.GeoStatus != GeoStatus.Confirmed);
                    var i = await q.OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Inst No", "Captured", "Site", "Zone", "Division", "Pole", "Item code", "Serial", "Officer", "Lat", "Lng", "Accuracy (m)", "Distance (m)", "Allowed (m)", "Geo status", "Verified by", "Justification", "Offline", "Age (d)", "Escalation level", "Field verification"];
                    rows = i.Where(x => sites.ContainsKey(x.SiteId)).Select(x => new object?[] { x.Number, x.CapturedAt, x.Site!.Code, x.Site.Zone, x.Site.Division, x.PoleId, x.Item!.Article!.ItemCode, x.Item.Serial, x.Officer, x.Lat, x.Lng, Math.Round(x.AccuracyM), Math.Round(x.DistanceM, 1), x.Site.GeofenceM, x.GeoStatus.ToString(), x.VerifiedBy, x.Justification, x.Offline ? "Yes" : "",
                        (int)((x.VerifiedAt ?? DateTime.UtcNow) - x.SubmittedAt).TotalDays, x.EscalationLevel, x.FvFindingsAt is not null ? "Findings: " + x.FvFindings : x.FvRequestedAt is not null ? "Requested " + x.FvRequestedAt.Value.AddHours(5).ToString("dd-MM-yyyy") : null }).ToList();
                    break;
                }
            case "consumption-site":
                {
                    var items = await db.Items.AsNoTracking().Include(x => x.Article).Where(x => x.SiteId != null).ToListAsync(ct);
                    cols = ["Site", "Name", "Zone", "Division", "Item code", "Description", "Issued", "Installed", "Pending", "Out of order / returned", "Value installed"];
                    rows = items.Where(x => sites.ContainsKey(x.SiteId!.Value) && (x.IssuedAt is null || (x.IssuedAt >= from && x.IssuedAt < to))).GroupBy(x => new { x.SiteId, x.ArticleId }).Select(g => { var s = sites[g.Key.SiteId!.Value]; var a = g.First().Article!; return new object?[] { s.Code, s.Name, s.Zone, s.Division, a.ItemCode, a.Description, g.Sum(x => x.Qty), g.Where(x => x.Status == ItemStatus.Installed).Sum(x => x.Qty), g.Where(x => x.Status is ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification).Sum(x => x.Qty), g.Where(x => x.Status is ItemStatus.OutOfOrder or ItemStatus.Dismantled or ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim or ItemStatus.Scrap or ItemStatus.Disposed).Sum(x => x.Qty), g.Where(x => x.Status == ItemStatus.Installed).Sum(x => x.Qty * a.Rate) }; }).OrderBy(r => (string)r[0]!).ThenBy(r => (string)r[4]!).ToList();
                    break;
                }
            case "consumption-po":
                {
                    var pos = await db.PurchaseOrders.AsNoTracking().Include(p => p.Vendor).Include(p => p.Lines).ThenInclude(l => l.Article).Where(p => f.PoId == null || p.Id == f.PoId).ToListAsync(ct);
                    var items = await db.Items.AsNoTracking().ToListAsync(ct);
                    cols = ["PO", "Vendor", "Line", "Item code", "Description", "Ordered", "Received", "In stock", "Issued", "Installed", "Failed", "Balance to receive", "Rate", "Value received"];
                    rows = pos.SelectMany(p => p.Lines.OrderBy(l => l.LineNo).Select(l =>
                    {
                        var its = l.Article is null ? new List<Item>() : items.Where(i => i.ArticleId == l.Article.Id).ToList();
                        var received = its.Where(i => i.Status != ItemStatus.Created && i.Status != ItemStatus.Rejected && i.Status != ItemStatus.Cancelled).Sum(i => i.Qty);
                        return new object?[] { p.Number, p.Vendor!.Name, l.LineNo, l.Article?.ItemCode, l.Description, l.Qty, received, its.Where(i => i.Status is ItemStatus.Received or ItemStatus.Reserved).Sum(i => i.Qty), its.Where(i => ItemLifecycle.InField(i.Status)).Sum(i => i.Qty), its.Where(i => i.Status == ItemStatus.Installed).Sum(i => i.Qty), its.Where(i => i.Status is ItemStatus.OutOfOrder or ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim or ItemStatus.Scrap or ItemStatus.Disposed).Sum(i => i.Qty), l.Qty - received, l.Rate, received * l.Rate };
                    })).ToList();
                    break;
                }
            case "returns":
            case "warranty":
            case "scrap":
                {
                    var q = Map.Returns(db).AsNoTracking().Where(x => x.ReportedAt >= from && x.ReportedAt < to);
                    if (key == "warranty") q = q.Where(x => x.Classification == ReturnClass.Warranty);
                    if (key == "scrap") q = q.Where(x => x.Classification == ReturnClass.Scrap);
                    var r = await q.OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Return No", "Reported", "Kind", "Site", "Item code", "Serial", "PO", "Vendor", "Installed on", "Service days", "Reason", "Received at store", "Store", "Classification", "Warranty eligible", "Warranty expires", "Claim ref", "Disposal approved", "Scrap value", "Closed"];
                    rows = r.Select(Map.Return).Select(x => new object?[] { x.Number, x.ReportedAt, x.Kind, x.SiteCode, x.ItemCode, x.QrCode.Split('|')[2], x.PoNumber, x.VendorName, x.InstalledAt, x.ServiceDays, x.Reason, x.ReceivedAt, x.StoreCode, x.Classification, x.WarrantyEligible ? "Yes" : "No", x.WarrantyExpires, x.ClaimRef, x.DisposalApprovedAt, x.ScrapValue, x.ClosedAt }).ToList();
                    break;
                }
            case "failures":
                {
                    var items = await db.Items.AsNoTracking().Include(x => x.Article).ThenInclude(a => a!.PurchaseOrder).ThenInclude(p => p!.Vendor).Where(x => x.InstalledAt != null || x.Status == ItemStatus.Installed).ToListAsync(ct);
                    var failedSet = new[] { ItemStatus.OutOfOrder, ItemStatus.Returned, ItemStatus.UnderRepair, ItemStatus.WarrantyClaim, ItemStatus.Scrap, ItemStatus.Disposed };
                    var notes = await db.ReturnNotes.AsNoTracking().Where(n => n.Kind == "OutOfOrder").ToListAsync(ct);
                    cols = ["Item code", "Description", "Vendor", "PO", "Ever installed", "Failed", "Failure rate %", "Avg days in service at failure", "Top reason"];
                    rows = items.GroupBy(x => x.ArticleId).Select(g =>
                    {
                        var a = g.First().Article!; var failed = g.Where(x => failedSet.Contains(x.Status)).ToList();
                        var fnotes = notes.Where(n => failed.Any(x => x.Id == n.ItemId)).ToList();
                        var avg = fnotes.Count == 0 ? (double?)null : Math.Round(fnotes.Average(n => (n.ReportedAt - (g.First(x => x.Id == n.ItemId).InstalledAt ?? n.ReportedAt)).TotalDays));
                        return new object?[] { a.ItemCode, a.Description, a.PurchaseOrder!.Vendor!.Name, a.PurchaseOrder.Number, g.Count(), failed.Count, Math.Round(100.0 * failed.Count / g.Count(), 1), avg, fnotes.GroupBy(n => n.Reason).OrderByDescending(x => x.Count()).FirstOrDefault()?.Key };
                    }).OrderByDescending(r => (double)r[6]!).ToList();
                    break;
                }
            case "ageing":
                {
                    var items = await db.Items.AsNoTracking().Include(x => x.Article).ThenInclude(a => a!.PurchaseOrder).Include(x => x.StoreLocation).ThenInclude(l => l!.Store).Where(x => (x.Status == ItemStatus.Received || x.Status == ItemStatus.Reserved) && x.StoreLocationId != null).ToListAsync(ct);
                    var now = DateTime.UtcNow;
                    cols = ["Store", "Location", "Item code", "Description", "PO", "0–90 d", "91–180 d", "181–365 d", "> 365 d", "Total qty", "Value"];
                    rows = items.Where(x => stores.ContainsKey(x.StoreLocation!.StoreId)).GroupBy(x => new { x.StoreLocationId, x.ArticleId }).Select(g =>
                    {
                        var a = g.First().Article!; var l = g.First().StoreLocation!;
                        decimal B(int lo, int hi) => g.Where(x => { var d = (now - (x.ReceivedAt ?? now)).TotalDays; return d >= lo && d < hi; }).Sum(x => x.Qty);
                        return new object?[] { l.Store!.Code, l.FullCode, a.ItemCode, a.Description, a.PurchaseOrder!.Number, B(0, 91), B(91, 181), B(181, 366), B(366, 100000), g.Sum(x => x.Qty), g.Sum(x => x.Qty * a.Rate) };
                    }).OrderBy(r => (string)r[0]!).ThenBy(r => (string)r[1]!).ToList();
                    break;
                }
            case "officers":
                {
                    var inst = await db.Installations.AsNoTracking().Where(x => x.SubmittedAt >= from && x.SubmittedAt < to).ToListAsync(ct);
                    var rets = await db.ReturnNotes.AsNoTracking().Where(x => x.ReportedAt >= from && x.ReportedAt < to).ToListAsync(ct);
                    var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
                    cols = ["Officer", "Installations", "Confirmed first time", "Exceptions", "Exception rate %", "Approved exceptions", "Rejected", "Returns reported", "Avg GPS accuracy (m)"];
                    rows = inst.Where(x => sites.ContainsKey(x.SiteId)).GroupBy(x => x.OfficerId).Select(g => new object?[] { names.GetValueOrDefault(g.Key, g.Key), g.Count(), g.Count(x => x.GeoStatus == GeoStatus.Confirmed), g.Count(x => x.GeoStatus != GeoStatus.Confirmed), Math.Round(100.0 * g.Count(x => x.GeoStatus != GeoStatus.Confirmed) / g.Count(), 1), g.Count(x => x.GeoStatus == GeoStatus.ApprovedException), g.Count(x => x.GeoStatus == GeoStatus.Rejected), rets.Count(r => r.ReportedById == g.Key), Math.Round(g.Average(x => x.AccuracyM)) }).OrderByDescending(r => (int)r[1]!).ToList();
                    break;
                }
            case "reconciliation":
                {
                    // FR-INV-016: Opening + Receipts + Returns + Transfers in − Issues − Transfers out − Disposals ± Adjustments = Closing,
                    // compared with what the item records hold now when the period runs to today
                    var mv = await db.StockMovements.AsNoTracking().Where(m => m.At < to).ToListAsync(ct);
                    mv = mv.Where(m => stores.ContainsKey(m.StoreId)).ToList();
                    var articles = await db.Articles.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
                    var toNow = f.To is null || f.To.Value.Date >= DateTime.UtcNow.AddHours(5).Date;
                    var onHand = toNow ? (await db.Items.AsNoTracking().Include(i => i.StoreLocation).Where(i => i.StoreLocationId != null && StoreControlService.InStore.Contains(i.Status)).ToListAsync(ct))
                        .GroupBy(i => (i.StoreLocation!.StoreId, i.ArticleId)).ToDictionary(g => g.Key, g => g.Sum(i => i.Qty)) : new();
                    var keys = mv.Select(m => (m.StoreId, m.ArticleId)).Concat(onHand.Keys).Distinct().Where(k => stores.ContainsKey(k.StoreId));
                    cols = ["Store", "Item code", "Description", "Opening", "Receipts", "Returns", "Transfers in", "Issues", "Transfers out", "Disposals", "Adjustments", "Closing (ledger)", "On hand (item records)", "Difference", "Result"];
                    rows = keys.Select(k =>
                    {
                        var g = mv.Where(m => m.StoreId == k.StoreId && m.ArticleId == k.ArticleId).ToList();
                        decimal S(params string[] t) => g.Where(m => m.At >= from && t.Contains(m.Type)).Sum(m => m.Qty);
                        var opening = g.Where(m => m.At < from).Sum(m => m.Qty);
                        var receipts = S("Receipt", "Opening", "ReceiptReversal"); var ret = S("Return"); var tin = S("TransferIn"); var iss = -S("Issue"); var tout = -S("TransferOut"); var disp = -S("Disposal"); var adj = S("Adjustment");
                        var closing = opening + receipts + ret + tin - iss - tout - disp + adj;
                        decimal? actual = toNow ? onHand.GetValueOrDefault(k) : null;
                        var diff = actual is null ? (decimal?)null : closing - actual.Value;
                        var a = articles.GetValueOrDefault(k.ArticleId);
                        return new object?[] { stores[k.StoreId].Code, a?.ItemCode, a?.Description, opening, receipts, ret, tin, iss, tout, disp, adj, closing, actual, diff, diff is null ? "—" : diff == 0 ? "Balances" : "DOES NOT BALANCE" };
                    }).Where(r => r.Skip(3).Take(10).Any(v => v is decimal d && d != 0)).OrderBy(r => (string)r[0]!).ThenBy(r => (string?)r[1]).ToList();
                    break;
                }
            case "adjustments":
                {
                    var a = await db.StockAdjustments.AsNoTracking().Include(x => x.Store).Include(x => x.Item).ThenInclude(i => i!.Article).Where(x => x.RaisedAt >= from && x.RaisedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Adjustment", "Raised", "Store", "Item code", "Serial", "Kind", "Reason code", "Qty before", "Qty after", "Status", "Raised by", "Decided by", "Decided", "Decision note", "Count"];
                    rows = a.Where(x => stores.ContainsKey(x.StoreId)).Select(x => new object?[] { x.Number, x.RaisedAt, x.Store!.Code, x.Item!.Article!.ItemCode, x.Item.Serial, x.Kind, x.ReasonCode, x.QtyBefore, x.QtyAfter, x.Status, x.RaisedBy, x.DecidedBy, x.DecidedAt, x.DecisionNote, x.PhysicalCountId }).ToList();
                    break;
                }
            case "transfers":
                {
                    var t = await db.StockTransfers.AsNoTracking().Include(x => x.FromStore).Include(x => x.ToStore).Where(x => x.DespatchedAt >= from && x.DespatchedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    var transit = await db.Items.Where(i => i.TransferId != null && i.Status == ItemStatus.InTransit).GroupBy(i => i.TransferId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                    cols = ["Transfer", "Despatched", "From", "To", "Labels", "Qty", "Still in transit", "Status", "Despatched by", "Vehicle", "Received by", "Received"];
                    rows = t.Where(x => stores.ContainsKey(x.FromStoreId) || stores.ContainsKey(x.ToStoreId)).Select(x => new object?[] { x.Number, x.DespatchedAt, x.FromStore!.Code, x.ToStore!.Code, x.ItemCount, x.Qty, transit.GetValueOrDefault(x.Id), x.Status, x.DespatchedBy, x.Vehicle, x.ReceivedBy, x.ReceivedAt }).ToList();
                    break;
                }
            case "evidence":
                {
                    var i = await Map.Installations(db).AsNoTracking().Where(x => x.SubmittedAt >= from && x.SubmittedAt < to).OrderByDescending(x => x.Id).ToListAsync(ct);
                    cols = ["Installation", "Photo", "Site", "Item code", "Serial", "Officer", "Captured", "Synchronised", "Offline", "Lat", "Lng", "Accuracy (m)", "SHA-256", "Stamped copy", "Device", "Status"];
                    rows = i.Where(x => sites.ContainsKey(x.SiteId)).SelectMany(x => new[] { x.Before!, x.After! }.Select(e => new object?[] { x.Number, e.Kind, x.Site!.Code, x.Item!.Article!.ItemCode, x.Item.Serial, x.Officer, e.CapturedAt, x.SubmittedAt, x.Offline ? "Yes" : "", e.Lat, e.Lng, e.AccuracyM is null ? null : Math.Round(e.AccuracyM.Value), e.Sha256, e.StampedPath is null ? "No" : "Yes", e.DeviceId?.ToString()[..8], x.GeoStatus.ToString() })).ToList();
                    break;
                }
            default: throw AppException.NotFound("Report");
        }
        return new ReportDto(key, title, cols, rows, filters);
    }
}
