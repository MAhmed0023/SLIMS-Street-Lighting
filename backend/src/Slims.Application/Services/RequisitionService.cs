using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Phase 2 — digital requisition, availability check, approval and stock reservation (FR-RQ-01 … FR-RQ-14).</summary>
public class RequisitionService(IAppDbContext db, AuditService audit, UserContext users, NumberingService numbers, ConfigService configs, NotificationService notify)
{
    /// <summary>Issuable stock at a store per article: Received items in active stock locations.</summary>
    public async Task<Dictionary<int, decimal>> AvailabilityAsync(int storeId, CancellationToken ct)
        => await db.Items.Where(i => i.Status == ItemStatus.Received && i.StoreLocation!.StoreId == storeId && i.StoreLocation.Kind == "Stock").GroupBy(i => i.ArticleId).Select(g => new { g.Key, Q = g.Sum(i => i.Qty) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);

    bool Mine(User me, Requisition r) => r.RaisedById == me.Id || (AccessPolicy.SiteVisible(me, r.Site!) && (me.RoleKey != "supervisor" || r.Site!.SupervisorUserId == me.Id || r.Site.SupervisorUserId == null));

    public async Task<List<RequisitionDto>> ListAsync(string? status, int? storeId, int? siteId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var q = Map.Requisitions(db).AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequisitionStatus>(status, true, out var st)) q = q.Where(r => r.Status == st);
        if (storeId is not null) q = q.Where(r => r.StoreId == storeId);
        if (siteId is not null) q = q.Where(r => r.SiteId == siteId);
        var rows = await q.OrderByDescending(r => r.Id).Take(600).ToListAsync(ct);
        var issues = await db.Issues.Where(i => rows.Select(r => r.Id).Contains(i.RequisitionId)).Select(i => new { i.RequisitionId, i.Id }).ToListAsync(ct);
        return rows.Where(r => AccessPolicy.StoreVisible(me, r.Store!) || Mine(me, r)).Select(r => Map.Requisition(r, null, issues.Where(i => i.RequisitionId == r.Id).Select(i => i.Id).ToList())).ToList();
    }

    public async Task<RequisitionDto> GetAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var r = await Map.Requisitions(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Requisition");
        if (!(AccessPolicy.StoreVisible(me, r.Store!) || Mine(me, r))) throw AppException.Forbidden("This requisition is outside your scope.", "scope");
        var avail = await AvailabilityAsync(r.StoreId, ct);
        var issues = await db.Issues.Where(i => i.RequisitionId == id).Select(i => i.Id).ToListAsync(ct);
        return Map.Requisition(r, avail, issues);
    }

    public async Task<RequisitionDto> CreateAsync(RequisitionUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Requisition, ct);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == r.SiteId, ct) ?? throw AppException.Rule("BR-007", "Choose a site from the site master. Free-text sites are not accepted.");
        UserContext.RequireSite(me, site);
        if (me.RoleKey == "supervisor" && site.SupervisorUserId is not null && site.SupervisorUserId != me.Id) throw AppException.Rule("BR-008", $"Site {site.Code} is assigned to another supervisor.");
        if (site.Status is "Completed" or "On hold") throw AppException.Bad($"Site {site.Code} is {site.Status}; material cannot be requisitioned for it.");
        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == r.StoreId && s.Active, ct) ?? throw AppException.NotFound("Store");
        var lines = (r.Lines ?? new()).Where(l => l.Qty > 0).GroupBy(l => l.ArticleId).Select(g => new ReqLineUpsert(g.Key, g.Sum(x => x.Qty))).ToList();
        if (lines.Count == 0) throw AppException.Bad("Add at least one item with a quantity.");
        var articles = await db.Articles.Where(a => lines.Select(l => l.ArticleId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var l in lines) { if (!articles.TryGetValue(l.ArticleId, out var a)) throw AppException.NotFound("Article"); if (a.Status != "Active") throw AppException.Bad($"{a.ItemCode} is {a.Status}."); }
        var req = new Requisition { Number = await numbers.NextAsync("REQ", Codes.Clean(site.Zone, 3), ct), SiteId = site.Id, StoreId = store.Id, RaisedById = me.Id, RaisedBy = me.Name, NeedBy = r.NeedBy, Purpose = r.Purpose, Status = RequisitionStatus.Draft };
        foreach (var l in lines) req.Lines.Add(new RequisitionLine { ArticleId = l.ArticleId, QtyRequested = l.Qty });
        db.Requisitions.Add(req);
        audit.Log("stock", "requisition-created", me, "requisition", req.Number, next: new { Site = site.Code, Store = store.Code, Lines = lines.Count, Qty = lines.Sum(l => l.Qty) });
        if (r.Submit) await SubmitInternalAsync(me, req, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(req.Id, ct);
    }

    async Task SubmitInternalAsync(User me, Requisition req, CancellationToken ct)
    {
        var avail = await AvailabilityAsync(req.StoreId, ct);
        foreach (var l in req.Lines)
        {
            var have = avail.GetValueOrDefault(l.ArticleId);
            l.Shortfall = Math.Max(0, l.QtyRequested - have);
            if (l.Shortfall > 0 && !l.IndentRaised)
            {
                // FR-RQ-06: automatic purchase indent for the shortfall — recorded for Procurement's indent report.
                l.IndentRaised = true;
                audit.Log("stock", "indent-raised", me, "requisition", req.Number, next: new { l.ArticleId, Shortfall = l.Shortfall, Store = req.StoreId }, reason: "stock short at submission");
            }
        }
        req.Status = RequisitionStatus.Submitted; req.SubmittedAt = DateTime.UtcNow;
        await SetLevelsAsync(req, ct);
        audit.Log("stock", "requisition-submitted", me, "requisition", req.Number, next: new { Shortfall = req.Lines.Sum(l => l.Shortfall), req.Value, req.RequiredLevel });
        var store = req.Store ?? await db.Stores.FirstAsync(s => s.Id == req.StoreId, ct);
        var site = req.Site ?? await db.Sites.FirstAsync(s => s.Id == req.SiteId, ct);
        await notify.NotifyAsync((await notify.ApproversAsync(req.CurrentLevel, store, ct)).Where(u => u.Id != me.Id), "NTF-01", $"Requisition {req.Number} awaiting your approval",
            $"{me.Name} requisitioned material worth Rs. {req.Value:N0} for {site.Code} from {store.Code}.", "/requisitions", "requisition", req.Number, ct, level: req.CurrentLevel);
    }

    /// <summary>FR-REQ-010: value at submission decides the level whose limit covers it.</summary>
    async Task SetLevelsAsync(Requisition req, CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        var ids = req.Lines.Select(l => l.ArticleId).ToList();
        var rates = await db.Articles.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Rate, ct);
        req.Value = req.Lines.Sum(l => l.QtyRequested * rates.GetValueOrDefault(l.ArticleId));
        var levels = cfg.ApprovalLevels.OrderBy(l => l.Level).ToList();
        req.RequiredLevel = levels.FirstOrDefault(l => l.MaxValue is null || l.MaxValue >= req.Value)?.Level ?? levels.LastOrDefault()?.Level ?? 1;
        req.CurrentLevel = levels.FirstOrDefault()?.Level ?? 1;
    }

    /// <summary>The approver's own level in the matrix; the System Administrator counts as the top level.</summary>
    static int LevelOf(User me, List<Domain.Config.ApprovalLevel> levels)
    {
        var mine = levels.Where(l => l.RoleKey == me.RoleKey).Select(l => l.Level).DefaultIfEmpty(0).Max();
        return mine == 0 && me.RoleKey == "sysadmin" ? levels.Select(l => l.Level).DefaultIfEmpty(1).Max() : mine;
    }

    public async Task<RequisitionDto> SubmitAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Requisition, ct);
        var req = await Map.Requisitions(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Requisition");
        if (req.RaisedById != me.Id && !AccessPolicy.Can(me, Permissions.Configure)) throw AppException.Forbidden("Only the supervisor who raised it can submit this requisition.");
        if (req.Status != RequisitionStatus.Draft) throw AppException.Bad($"Requisition is {req.Status}.");
        await SubmitInternalAsync(me, req, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>FR-RQ-07/08/10: approve (in full or part) and ring-fence stock; or reject with a reason.</summary>
    public async Task<RequisitionDto> DecideAsync(int id, RequisitionDecision d, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Approve, ct);
        var req = await Map.Requisitions(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Requisition");
        UserContext.RequireStore(me, req.Store!);
        UserContext.RequireSite(me, req.Site!);
        if (req.Status != RequisitionStatus.Submitted) throw AppException.Bad($"Requisition is {req.Status}; only submitted requisitions can be decided.");
        if (req.RaisedById == me.Id) throw AppException.Rule("BR-026", "You cannot approve a requisition you raised yourself.");
        var cfg = await configs.GetAsync(ct);
        var levels = cfg.ApprovalLevels.OrderBy(l => l.Level).ToList();
        if (req.RequiredLevel == 0) await SetLevelsAsync(req, ct);
        var myLevel = LevelOf(me, levels);
        string Label(int lv) => levels.FirstOrDefault(l => l.Level == lv)?.Label ?? $"level {lv}";
        if (myLevel == 0) throw AppException.Forbidden($"Your role is not in the approval matrix (Parameters ▸ Approval levels).", "FR-REQ-010");
        if (myLevel < req.CurrentLevel) throw AppException.Rule("FR-REQ-010", $"Requisition {req.Number} is with {Label(req.CurrentLevel)} (level {req.CurrentLevel}); your level is {myLevel}.");
        if (d.Approve && myLevel < req.RequiredLevel)
        {
            // above this officer's limit: the approval becomes a recommendation and the requisition escalates
            var next = levels.FirstOrDefault(l => l.Level > myLevel)?.Level ?? req.RequiredLevel;
            req.CurrentLevel = next; req.EscalatedAt = DateTime.UtcNow;
            req.RecommendedBy = ((req.RecommendedBy ?? "") + $"{me.Name} ({Label(myLevel)}) {DateTime.UtcNow.AddHours(5):dd-MM-yyyy HH:mm}; ").TrimStart();
            audit.Log("stock", "requisition-recommended", me, "requisition", req.Number, next: new { req.Value, Limit = levels.First(l => l.Level == myLevel).MaxValue, EscalatedTo = Label(next) }, reason: d.Note);
            await notify.NotifyAsync((await notify.ApproversAsync(next, req.Store!, ct)).Where(u => u.Id != req.RaisedById), "NTF-01", $"Requisition {req.Number} escalated for your approval",
                $"Rs. {req.Value:N0} is above the limit of {Label(myLevel)}. Recommended by {me.Name}. {d.Note}", "/requisitions?id=" + req.Id, "requisition", req.Number, ct, level: next);
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct);
        }
        req.DecidedAt = DateTime.UtcNow; req.ApprovedById = me.Id; req.ApprovedBy = me.Name; req.DecisionNote = d.Note;
        if (!d.Approve)
        {
            if (string.IsNullOrWhiteSpace(d.Note)) throw AppException.Bad("A reason is required to reject.");
            req.Status = RequisitionStatus.Rejected;
            audit.Log("stock", "requisition-rejected", me, "requisition", req.Number, reason: d.Note);
            await notify.NotifyAsync(await notify.ByIdsAsync(ct, req.RaisedById), "NTF-03", $"Requisition {req.Number} rejected", $"Rejected by {me.Name}: {d.Note}", "/requisitions?id=" + req.Id, "requisition", req.Number, ct);
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct);
        }
        var decided = (d.Lines ?? new()).ToDictionary(l => l.LineId, l => l.QtyApproved);
        var reservedAny = false;
        foreach (var l in req.Lines)
        {
            var want = decided.TryGetValue(l.Id, out var q) ? q : l.QtyRequested;
            if (want < 0 || want > l.QtyRequested) throw AppException.Bad($"Approved quantity for {l.Article!.ItemCode} must be between 0 and {l.QtyRequested}.");
            // BR-009: reserve FIFO by receipt date from the store's stock locations
            var pool = await db.Items.Include(i => i.StoreLocation).Where(i => i.ArticleId == l.ArticleId && i.Status == ItemStatus.Received && i.StoreLocation!.StoreId == req.StoreId && i.StoreLocation.Kind == "Stock").OrderBy(i => i.ReceivedAt).ThenBy(i => i.Serial).ToListAsync(ct);
            decimal got = 0;
            foreach (var i in pool)
            {
                if (got >= want) break;
                i.Status = ItemStatus.Reserved; i.RequisitionId = req.Id; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id; got += i.Qty; reservedAny = true;
                audit.Log("stock", "reserved", me, "item", i.Id.ToString(), next: new { req.Number });
            }
            l.QtyApproved = Math.Min(want, got); l.Shortfall = Math.Max(0, want - got);
        }
        if (!reservedAny) throw AppException.Rule("FR-RQ-05", "Nothing could be reserved — the store has no issuable stock for these items. Reject, or wait for the indent to be received.");
        req.Status = RequisitionStatus.Approved; req.IssueSlipNo = await numbers.NextAsync("ISL", req.Store!.Code, ct);
        audit.Log("stock", "requisition-approved", me, "requisition", req.Number, next: new { req.IssueSlipNo, Approved = req.Lines.Sum(l => l.QtyApproved), Shortfall = req.Lines.Sum(l => l.Shortfall), Level = myLevel }, reason: d.Note);
        await notify.NotifyAsync([.. await notify.ByIdsAsync(ct, req.RaisedById), .. await notify.StoreStaffAsync(req.Store!, ct)], "NTF-02", $"Requisition {req.Number} approved",
            $"Issue slip {req.IssueSlipNo}: {req.Lines.Sum(l => l.QtyApproved):0.###} reserved for {req.Site!.Code} at {req.Store!.Code}. Reservation valid {cfg.Controls.ReservationDays} day(s).", "/requisitions?id=" + req.Id, "requisition", req.Number, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<RequisitionDto> CancelAsync(int id, string? reason, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Requisition, Permissions.Approve);
        var req = await Map.Requisitions(db).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Requisition");
        if (req.Status is RequisitionStatus.Issued or RequisitionStatus.PartIssued or RequisitionStatus.Cancelled) throw AppException.Bad($"Requisition is {req.Status} and cannot be cancelled.");
        if (req.RaisedById != me.Id && !AccessPolicy.Can(me, Permissions.Approve)) throw AppException.Forbidden("Only the raiser or an approver can cancel.");
        // DF-06: an approver may only cancel within their own store and site scope
        if (req.RaisedById != me.Id) { UserContext.RequireStore(me, req.Store!); UserContext.RequireSite(me, req.Site!); }
        await ReleaseAsync(me, req, "requisition cancelled", ct);
        req.Status = RequisitionStatus.Cancelled; req.DecisionNote = reason ?? req.DecisionNote;
        audit.Log("stock", "requisition-cancelled", me, "requisition", req.Number, reason: reason);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task ReleaseAsync(User me, Requisition req, string reason, CancellationToken ct)
    {
        var items = await db.Items.Where(i => i.RequisitionId == req.Id && i.Status == ItemStatus.Reserved).ToListAsync(ct);
        foreach (var i in items) { i.Status = ItemStatus.Received; i.RequisitionId = null; i.UpdatedAt = DateTime.UtcNow; i.UpdatedBy = me.Id; audit.Log("stock", "reservation-released", me, "item", i.Id.ToString(), reason: reason); }
    }
}
