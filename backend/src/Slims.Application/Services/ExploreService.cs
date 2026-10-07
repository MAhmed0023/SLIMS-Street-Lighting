using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

public record DrillCrumb(string Label, string Level, string? Key);
public record DrillRow(object?[] Cells, string? Level, string? Key);
public record DrillDto(string Level, string Title, List<DrillCrumb> Path, string[] Columns, List<DrillRow> Rows, ItemDetailDto? Item, InstallationDto? Validation);
public record MapSiteDto(int Id, string Code, string Name, string Zone, string Division, double Lat, double Lng, int GeofenceM, string Status, int Issued, int Installed, int Pending, int Exceptions, DateTime? LastInstallation, string Marker);
public record MapPointDto(int Id, string Number, int SiteId, string SiteCode, double Lat, double Lng, string GeoStatus, string ItemCode, int Serial, string Officer, DateTime CapturedAt, double DistanceM);
public record MapDto(List<MapSiteDto> Sites, List<MapPointDto> Installations, string[] Zones);

/// <summary>FR-RPT-005 drill-down (Summary → area → site → requisition / issue → item → evidence) and the FR-RPT-009 map.
/// Everything is filtered to the viewer's scope.</summary>
public class ExploreService(IAppDbContext db, UserContext users, MasterService masters)
{
    static readonly ItemStatus[] Pending = [ItemStatus.Issued, ItemStatus.AtSite, ItemStatus.PendingVerification];

    public async Task<DrillDto> DrillAsync(string? level, string? key, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var sites = (await db.Sites.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.SiteVisible(me, s)).ToList();
        var siteIds = sites.Select(s => s.Id).ToHashSet();
        var items = (await db.Items.AsNoTracking().Where(i => i.SiteId != null).Select(i => new { i.SiteId, i.Status, i.Qty }).ToListAsync(ct)).Where(i => siteIds.Contains(i.SiteId!.Value)).ToList();
        var exc = (await db.Installations.AsNoTracking().Where(x => x.GeoStatus == GeoStatus.PendingVerification).Select(x => x.SiteId).ToListAsync(ct)).Where(siteIds.Contains).ToList();
        object?[] Counts(IEnumerable<int> ids)
        {
            var set = ids.ToHashSet(); var its = items.Where(i => set.Contains(i.SiteId!.Value)).ToList();
            return [its.Sum(i => i.Qty), its.Where(i => i.Status == ItemStatus.Installed).Sum(i => i.Qty), its.Where(i => Pending.Contains(i.Status)).Sum(i => i.Qty), exc.Count(set.Contains)];
        }
        var root = new DrillCrumb("All areas", "summary", null);
        switch (level ?? "summary")
        {
            case "summary":
                return new("summary", "Summary by zone", [root], ["Zone", "Sites", "Issued", "Installed", "Pending", "Open exceptions"],
                    sites.GroupBy(s => s.Zone).OrderBy(g => g.Key).Select(g => new DrillRow([g.Key, g.Count(), .. Counts(g.Select(s => s.Id))], "zone", g.Key)).ToList(), null, null);
            case "zone":
                {
                    var z = sites.Where(s => s.Zone == key).ToList();
                    if (z.Count == 0) throw AppException.NotFound("Zone");
                    return new("zone", $"Zone {key}", [root, new($"Zone {key}", "zone", key)], ["Division", "Site", "Name", "Status", "Issued", "Installed", "Pending", "Open exceptions"],
                        z.OrderBy(s => s.Division).ThenBy(s => s.Code).Select(s => new DrillRow([s.Division, s.Code, s.Name, s.Status, .. Counts([s.Id])], "site", s.Id.ToString())).ToList(), null, null);
                }
            case "site":
                {
                    var s = sites.FirstOrDefault(x => x.Id.ToString() == key) ?? throw AppException.NotFound("Site");
                    var reqs = await Map.Requisitions(db).AsNoTracking().Where(r => r.SiteId == s.Id).OrderByDescending(r => r.Id).ToListAsync(ct);
                    var iss = await db.Issues.AsNoTracking().Where(i => i.SiteId == s.Id).OrderByDescending(i => i.Id).ToListAsync(ct);
                    var rows = reqs.Select(r => new DrillRow(["Requisition", r.Number, r.RaisedAt, r.Status.ToString(), r.Lines.Sum(l => l.QtyRequested), r.Lines.Sum(l => l.QtyIssued)], "requisition", r.Id.ToString()))
                        .Concat(iss.Select(i => new DrillRow(["Issue", i.Number, i.IssuedAt, i.ReceivedAt is null ? "Not acknowledged" : "Acknowledged", i.Qty, i.ItemCount], "issue", i.Id.ToString()))).OrderByDescending(r => (DateTime)r.Cells[2]!).ToList();
                    return new("site", $"{s.Code} — {s.Name}", [root, new($"Zone {s.Zone}", "zone", s.Zone), new(s.Code, "site", key)], ["Document", "Number", "Date", "Status", "Quantity", "Issued / labels"], rows, null, null);
                }
            case "requisition":
            case "issue":
                {
                    int id = int.TryParse(key, out var k) ? k : 0;
                    int siteId; string title; List<Item> its;
                    if (level == "requisition")
                    {
                        var r = await db.Requisitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Requisition");
                        siteId = r.SiteId; title = "Requisition " + r.Number;
                        its = await Map.Items(db).AsNoTracking().Where(i => i.RequisitionId == id || db.Issues.Any(x => x.RequisitionId == id && x.Id == i.IssueId)).ToListAsync(ct);
                    }
                    else
                    {
                        var x = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Issue");
                        siteId = x.SiteId; title = "Issue " + x.Number;
                        var moved = await db.StockMovements.AsNoTracking().Where(m => m.Type == "Issue" && m.RefNo == x.Number).Select(m => m.ItemId).ToListAsync(ct);
                        its = await Map.Items(db).AsNoTracking().Where(i => i.IssueId == id || moved.Contains(i.Id)).ToListAsync(ct);
                    }
                    var s = sites.FirstOrDefault(x => x.Id == siteId) ?? throw AppException.Forbidden("This document is outside your scope.", "scope");
                    return new(level, title, [root, new($"Zone {s.Zone}", "zone", s.Zone), new(s.Code, "site", s.Id.ToString()), new(title, level, key)], ["Item code", "Serial", "Description", "Status", "Installed", "Location / site"],
                        its.OrderBy(i => i.Article!.ItemCode).ThenBy(i => i.Serial).Select(i => new DrillRow([i.Article!.ItemCode, i.Serial, i.Article.Description, i.Status.ToString(), i.InstalledAt, i.StoreLocation?.FullCode ?? i.Site?.Code], "item", i.Id.ToString())).ToList(), null, null);
                }
            case "item":
                {
                    var detail = await masters.ItemAsync(key ?? "", ct);
                    var it = detail.Item;
                    var s = it.SiteId is null ? null : sites.FirstOrDefault(x => x.Id == it.SiteId);
                    if (it.SiteId is not null && s is null) throw AppException.Forbidden("This item is outside your scope.", "scope");
                    var path = new List<DrillCrumb> { root };
                    if (s is not null) { path.Add(new($"Zone {s.Zone}", "zone", s.Zone)); path.Add(new(s.Code, "site", s.Id.ToString())); }
                    if (it.IssueId is not null) path.Add(new("Issue", "issue", it.IssueId.ToString()));
                    path.Add(new($"{it.ItemCode} #{it.Serial}", "item", key));
                    return new("item", $"{it.ItemCode} #{it.Serial}", path, [], new(), detail, detail.Installation);
                }
            default: throw AppException.Bad("Unknown drill-down level.");
        }
    }

    public async Task<MapDto> MapAsync(string? zone, int? siteId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var sites = (await db.Sites.AsNoTracking().ToListAsync(ct)).Where(s => AccessPolicy.SiteVisible(me, s)).ToList();
        var zones = sites.Select(s => s.Zone).Distinct().OrderBy(z => z).ToArray();
        sites = sites.Where(s => (string.IsNullOrEmpty(zone) || s.Zone == zone) && (siteId is null || s.Id == siteId)).ToList();
        var ids = sites.Select(s => s.Id).ToHashSet();
        var items = (await db.Items.AsNoTracking().Where(i => i.SiteId != null).Select(i => new { i.SiteId, i.Status, i.Qty }).ToListAsync(ct)).Where(i => ids.Contains(i.SiteId!.Value)).ToList();
        var fromD = from ?? DateTime.MinValue; var toD = (to ?? DateTime.MaxValue.AddDays(-1)).Date.AddDays(1);
        var inst = (await Map.Installations(db).AsNoTracking().Where(x => x.SubmittedAt >= fromD && x.SubmittedAt < toD && x.GeoStatus != GeoStatus.Rejected).ToListAsync(ct)).Where(x => ids.Contains(x.SiteId)).ToList();
        var lastInst = inst.GroupBy(x => x.SiteId).ToDictionary(g => g.Key, g => g.Max(x => x.CapturedAt));
        var mapSites = sites.Select(s =>
        {
            var its = items.Where(i => i.SiteId == s.Id).ToList();
            var issued = its.Count; var installed = its.Count(i => i.Status == ItemStatus.Installed); var pending = its.Count(i => Pending.Contains(i.Status));
            var exceptions = its.Count(i => i.Status == ItemStatus.PendingVerification);
            var marker = exceptions > 0 ? "exception" : pending > 0 ? "pending" : installed > 0 ? "installed" : "planned";
            return new MapSiteDto(s.Id, s.Code, s.Name, s.Zone, s.Division, s.Lat, s.Lng, s.GeofenceM, s.Status, issued, installed, pending, exceptions, lastInst.TryGetValue(s.Id, out var d) ? d : null, marker);
        }).ToList();
        var points = inst.Select(x => new MapPointDto(x.Id, x.Number, x.SiteId, x.Site!.Code, x.Lat, x.Lng, x.GeoStatus.ToString(), x.Item!.Article!.ItemCode, x.Item.Serial, x.Officer, x.CapturedAt, Math.Round(x.DistanceM, 1))).ToList();
        return new(mapSites, points, zones);
    }
}

/// <summary>What the SLIMS Field app loads when it opens or comes back online: the officer's sites, work list, re-capture
/// list and verification tasks, plus the thresholds it checks locally before capture.</summary>
public class MobileService(IAppDbContext db, UserContext users, ConfigService configs, DeviceService devices, MasterService masters, IssueService issues, InboxService inbox)
{
    public async Task<MobileBootstrapDto> BootstrapAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var dev = await devices.CurrentDeviceAsync(me, ct);
        var cfg = await configs.GetAsync(ct);
        var sites = (await masters.SitesAsync(null, null, ct)).Where(s => s.Status is not ("Completed") && (me.RoleKey != "officer" || s.OfficerUserId == null || s.OfficerUserId == me.Id)).ToList();
        var siteIds = sites.Select(s => s.Id).ToHashSet();
        var work = AccessPolicy.Can(me, Permissions.Install) ? (await issues.FieldItemsAsync(null, ct)).Where(i => i.SiteId is not null && siteIds.Contains(i.SiteId.Value)).ToList() : new();
        var ack = AccessPolicy.Can(me, Permissions.Install) || AccessPolicy.Can(me, Permissions.Requisition) ? (await issues.ListAsync(null, null, true, ct)).Where(i => siteIds.Contains(i.SiteId)).ToList() : new();
        var since = DateTime.UtcNow.AddDays(-60);
        var recapture = (await Map.Installations(db).AsNoTracking().Where(x => (x.GeoStatus == GeoStatus.Rejected || x.GeoStatus == GeoStatus.MissingEvidence) && x.SubmittedAt >= since && x.Item!.Status == ItemStatus.AtSite && x.Item.InstallationId == null && (x.Justification == null || x.Justification != "Superseded by re-captured evidence")).OrderByDescending(x => x.Id).ToListAsync(ct))
            .Where(x => siteIds.Contains(x.SiteId) && (me.RoleKey != "officer" || x.OfficerId == me.Id)).GroupBy(x => x.ItemId).Select(g => Map.Installation(g.First())).ToList();
        var tasks = (await Map.Installations(db).AsNoTracking().Where(x => x.FvAssignedToId == me.Id && x.FvFindingsAt == null && x.GeoStatus == GeoStatus.PendingVerification).OrderBy(x => x.FvRequestedAt).ToListAsync(ct)).Select(Map.Installation).ToList();
        return new(UserContext.ToDto(me), DeviceService.ToDto(dev, me), cfg.Geo, cfg.Field, sites, work, ack, recapture, tasks, cfg.FailureReasons, await inbox.UnreadAsync(ct), DateTime.UtcNow);
    }
}
