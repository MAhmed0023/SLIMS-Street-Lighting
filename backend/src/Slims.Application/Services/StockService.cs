using Microsoft.EntityFrameworkCore;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Stock position, ageing and the executive dashboard (FR-IC-01 … FR-IC-07, RPT-20).</summary>
public class StockService(IAppDbContext db, UserContext users, ConfigService configs, ReturnService returns)
{
    public async Task<StockSummaryDto> PositionAsync(int? storeId, string? q, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var cfg = await configs.GetAsync(ct);
        var stores = (await db.Stores.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.StoreVisible(me, s) && (storeId is null || s.Id == storeId)).Select(s => s.Id).ToHashSet();
        var items = await db.Items.AsNoTracking().Include(i => i.Article).Include(i => i.StoreLocation).ThenInclude(l => l!.Store)
            .Where(i => i.StoreLocationId != null && (i.Status == ItemStatus.Received || i.Status == ItemStatus.Reserved || i.Status == ItemStatus.Returned || i.Status == ItemStatus.UnderRepair || i.Status == ItemStatus.WarrantyClaim || i.Status == ItemStatus.Scrap)).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var rows = items.Where(i => stores.Contains(i.StoreLocation!.StoreId))
            .Where(i => string.IsNullOrWhiteSpace(q) || (i.Article!.ItemCode + " " + i.Article.Description + " " + i.StoreLocation!.FullCode).Contains(q, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => new { i.ArticleId, i.StoreLocationId })
            .Select(g =>
            {
                var a = g.First().Article!; var l = g.First().StoreLocation!;
                var onHand = g.Sum(i => i.Qty); var reserved = g.Where(i => i.Status == ItemStatus.Reserved).Sum(i => i.Qty);
                var available = l.Kind == "Stock" ? g.Where(i => i.Status == ItemStatus.Received).Sum(i => i.Qty) : 0;
                var oldest = g.Min(i => (i.ReceivedAt ?? i.ReturnedAt ?? now));
                return new StockRowDto(a.Id, a.ItemCode, a.Description, a.Category, a.Uom, l.StoreId, l.Store!.Code, l.Id, l.FullCode, l.Kind, onHand, reserved, available, onHand * a.Rate, (int)(now - oldest).TotalDays);
            }).OrderBy(r => r.StoreCode).ThenBy(r => r.LocationCode).ThenBy(r => r.ItemCode).ToList();
        var stockOnly = rows.Where(r => r.LocationKind == "Stock").ToList();
        var byArticle = stockOnly.GroupBy(r => r.ArticleId).ToDictionary(g => g.Key, g => g.Sum(r => r.Available));
        var articles = await db.Articles.AsNoTracking().Include(a => a.PurchaseOrder).Include(a => a.Vendor).Where(a => a.Status == "Active" && a.ReorderLevel > 0).ToListAsync(ct);
        var below = articles.Where(a => byArticle.GetValueOrDefault(a.Id) < a.ReorderLevel).Select(a => Map.Article(a, 0, 0, byArticle.GetValueOrDefault(a.Id), 0, 0, 0)).ToList();
        return new StockSummaryDto(rows, stockOnly.Sum(r => r.Value), stockOnly.Sum(r => r.OnHand), stockOnly.Select(r => r.ArticleId).Distinct().Count(), items.Count(i => stores.Contains(i.StoreLocation!.StoreId) && i.StoreLocation.Kind == "Stock"), below);
    }

    public async Task<List<ItemDto>> LocationItemsAsync(int locationId, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return (await Map.Items(db).AsNoTracking().Where(i => i.StoreLocationId == locationId).OrderBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).ToListAsync(ct)).Select(Map.Item).ToList();
    }

    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var cfg = await configs.GetAsync(ct);
        var now = DateTime.UtcNow; var monthStart = new DateTime(now.Year, now.Month, 1);
        var sites = (await db.Sites.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.SiteVisible(me, s)).ToDictionary(s => s.Id);
        var stores = (await db.Stores.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.StoreVisible(me, s)).ToDictionary(s => s.Id);
        var items = await db.Items.AsNoTracking().Include(i => i.Article).Include(i => i.StoreLocation).ToListAsync(ct);
        var visible = items.Where(i => (i.StoreLocation is not null && stores.ContainsKey(i.StoreLocation.StoreId)) || (i.SiteId is not null && sites.ContainsKey(i.SiteId.Value)) || (i.StoreLocation is null && i.SiteId is null && AccessPolicy.AllZones(me))).ToList();
        var stock = visible.Where(i => i.Status is ItemStatus.Received or ItemStatus.Reserved && i.StoreLocation?.Kind == "Stock").ToList();
        var installed = visible.Where(i => i.Status == ItemStatus.Installed).ToList();
        var installsMonth = await db.Installations.AsNoTracking().Include(x => x.Site).Where(x => x.SubmittedAt >= monthStart && (x.GeoStatus == GeoStatus.Confirmed || x.GeoStatus == GeoStatus.ApprovedException)).ToListAsync(ct);
        installsMonth = installsMonth.Where(x => sites.ContainsKey(x.SiteId)).ToList();
        var exceptions = await Map.Installations(db).AsNoTracking().Where(x => x.GeoStatus == GeoStatus.PendingVerification).OrderByDescending(x => x.Id).Take(50).ToListAsync(ct);
        var pending = await Map.Requisitions(db).AsNoTracking().Where(r => r.Status == RequisitionStatus.Submitted).OrderBy(r => r.RaisedAt).Take(50).ToListAsync(ct);
        var unack = await Map.Issues(db).AsNoTracking().Where(i => i.ReceivedAt == null && i.IssuedAt < now.AddHours(-cfg.Controls.AckOverdueHours)).OrderBy(i => i.IssuedAt).Take(50).ToListAsync(ct);
        var overdue = await returns.OverdueAsync(ct);
        var quarantine = visible.Count(i => i.Status is ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim);
        var failed = visible.Count(i => i.Status is ItemStatus.OutOfOrder or ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim or ItemStatus.Scrap or ItemStatus.Disposed);
        var everInstalled = installed.Count + failed;
        var ageing = stock.Count(i => i.ReceivedAt is DateTime d && (now - d).TotalDays > cfg.Controls.AgeingDays);

        var kpis = new List<KpiDto>
        {
            new("stock", "Stock on hand", stock.Sum(i => i.Qty), $"{cfg.App.Currency} {stock.Sum(i => i.Qty * i.Article!.Rate):N0} · {stock.Count} labels", ""),
            new("installed", "Installed & verified", installed.Count, $"{installsMonth.Count} this month", "good"),
            new("field", "In the field, not installed", visible.Count(i => i.Status is ItemStatus.Issued or ItemStatus.AtSite), $"{visible.Count(i => i.Status == ItemStatus.PendingVerification)} pending geo-verification", "watch"),
            new("exceptions", "Geo-fence exceptions", exceptions.Count(x => sites.ContainsKey(x.SiteId)), "awaiting supervisor verification", exceptions.Count == 0 ? "good" : "crit"),
            new("approvals", "Requisitions awaiting approval", pending.Count(r => stores.ContainsKey(r.StoreId)), pending.Count > 0 ? $"oldest {(now - pending[0].RaisedAt).TotalDays:0} d" : "queue clear", pending.Count > 5 ? "watch" : ""),
            new("quarantine", "In quarantine / repair / claim", quarantine, $"{overdue.Count} return(s) overdue from site", overdue.Count > 0 ? "watch" : ""),
            new("failure", "Failure rate", everInstalled == 0 ? 0 : Math.Round(100m * failed / everInstalled, 1), $"{failed} of {everInstalled} ever installed", failed > 0 && 100m * failed / Math.Max(1, everInstalled) > 5 ? "crit" : ""),
            new("ageing", "Stock older than " + cfg.Controls.AgeingDays + " days", ageing, "labels not issued", ageing > 0 ? "watch" : "good"),
        };
        var months = Enumerable.Range(0, 12).Select(k => monthStart.AddMonths(k - 11)).ToList();
        var allInst = await db.Installations.AsNoTracking().Where(x => x.SubmittedAt >= months[0]).Select(x => new { x.SubmittedAt, x.GeoStatus, x.SiteId }).ToListAsync(ct);
        var rets = await db.ReturnNotes.AsNoTracking().Where(x => x.ReportedAt >= months[0] && x.Kind != "Unused").Select(x => new { x.ReportedAt, x.SiteId }).ToListAsync(ct);
        var monthly = months.Select(m => new SeriesPoint(m.ToString("MMM yy"),
            allInst.Count(x => sites.ContainsKey(x.SiteId) && x.SubmittedAt >= m && x.SubmittedAt < m.AddMonths(1) && x.GeoStatus != GeoStatus.Rejected),
            rets.Count(x => x.SiteId is not null && sites.ContainsKey(x.SiteId.Value) && x.ReportedAt >= m && x.ReportedAt < m.AddMonths(1)))).ToList();
        var byZone = sites.Values.GroupBy(s => s.Zone).OrderBy(g => g.Key).Select(g => new GroupRow(g.Key, g.Key,
            installed.Count(i => i.SiteId is not null && g.Any(s => s.Id == i.SiteId)),
            visible.Count(i => i.SiteId is not null && g.Any(s => s.Id == i.SiteId) && i.Status is ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification),
            visible.Count(i => i.SiteId is not null && g.Any(s => s.Id == i.SiteId) && i.Status is ItemStatus.OutOfOrder or ItemStatus.Dismantled),
            g.Count())).ToList();
        var byCategory = visible.GroupBy(i => i.Article!.Category).OrderBy(g => g.Key).Select(g => new GroupRow(g.Key, cfg.Categories.FirstOrDefault(c => c.Code == g.Key)?.Label ?? g.Key,
            g.Where(i => i.Status is ItemStatus.Received or ItemStatus.Reserved).Sum(i => i.Qty), g.Where(i => i.Status == ItemStatus.Installed).Sum(i => i.Qty), g.Where(i => ItemLifecycle.InField(i.Status) && i.Status != ItemStatus.Installed).Sum(i => i.Qty), g.Where(i => i.Status is ItemStatus.Returned or ItemStatus.UnderRepair or ItemStatus.WarrantyClaim or ItemStatus.Scrap).Sum(i => i.Qty))).ToList();
        var byStatus = visible.GroupBy(i => i.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());
        return new DashboardDto(kpis, monthly, byZone, byCategory, exceptions.Where(x => sites.ContainsKey(x.SiteId)).Select(Map.Installation).ToList(),
            pending.Where(r => stores.ContainsKey(r.StoreId) || sites.ContainsKey(r.SiteId)).Select(r => Map.Requisition(r, null, null)).ToList(), overdue, unack.Where(i => stores.ContainsKey(i.StoreId) || sites.ContainsKey(i.SiteId)).Select(i => Map.Issue(i, new())).ToList(), byStatus, cfg.App, now);
    }
}
