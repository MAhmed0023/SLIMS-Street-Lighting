using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Slims.Application.Common;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

public record AutomationResult(DateTime RanAt, Dictionary<string, int> Counts, List<string> Errors);

/// <summary>The time-driven rules, run by the background worker every few minutes (and on demand by the administrator):
/// reservation lapse (FR-REQ-014), escalation of work not actioned (FR-NTF-004, FR-EXC-010), the configurable alerts
/// (FR-NTF-003), scheduled report delivery (FR-RPT-010) and the e-mail / SMS copies of notifications (FR-NTF-002).</summary>
public class AutomationService(IAppDbContext db, AuditService audit, ConfigService configs, NotificationService notify, NotifySettingsService channels, ScheduleService schedules, IMessageSender sender, ILogger<AutomationService> log)
{
    const string System = "system";
    static string Week(DateTime d) => $"{ISOWeek.GetYear(d)}-W{ISOWeek.GetWeekOfYear(d):00}";

    public async Task<AutomationResult> RunAsync(CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(); var errors = new List<string>();
        async Task Step(string name, Func<Task<int>> f)
        {
            try { counts[name] = await f(); await db.SaveChangesAsync(ct); }
            catch (Exception e) when (e is not OperationCanceledException) { errors.Add($"{name}: {e.Message}"); log.LogWarning(e, "Automation step {step} failed", name); }
        }
        await Step("reservations", () => ReservationsAsync(ct));
        await Step("approvals", () => ApprovalEscalationAsync(ct));
        await Step("acknowledgement", () => UnacknowledgedAsync(ct));
        await Step("installPending", () => InstallPendingAsync(ct));
        await Step("exceptions", () => ExceptionAgeingAsync(ct));
        await Step("recapture", () => RecaptureAsync(ct));
        await Step("returnsOverdue", () => ReturnsOverdueAsync(ct));
        await Step("pendingApprovals", () => PendingApprovalsAsync(ct));
        await Step("reorder", () => ReorderAsync(ct));
        await Step("gpsMismatch", () => GpsMismatchAsync(ct));
        await Step("returnRate", () => ReturnRateAsync(ct));
        await Step("unresolvedSites", () => UnresolvedSitesAsync(ct));
        await Step("turnaround", () => TurnaroundAsync(ct));
        await Step("ageing", () => AgeingAsync(ct));
        await Step("schedules", () => schedules.RunDueAsync(ct));
        await Step("delivery", () => DeliverAsync(ct));
        return new(DateTime.UtcNow, counts, errors);
    }

    // ---- FR-REQ-014 / NTF-04 --------------------------------------------------------------------------
    async Task<int> ReservationsAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var days = Math.Max(1, cfg.Controls.ReservationDays); var n = 0;
        var open = await Map.Requisitions(db).Where(r => (r.Status == RequisitionStatus.Approved || r.Status == RequisitionStatus.PartIssued) && r.DecidedAt != null).ToListAsync(ct);
        foreach (var r in open)
        {
            var expires = r.DecidedAt!.Value.AddDays(days);
            var who = new List<User>([.. await notify.ByIdsAsync(ct, r.RaisedById), .. await notify.StoreStaffAsync(r.Store!, ct)]);
            if (expires <= now)
            {
                var reserved = await db.Items.Where(i => i.RequisitionId == r.Id && i.Status == ItemStatus.Reserved).ToListAsync(ct);
                foreach (var i in reserved) { i.Status = ItemStatus.Received; i.RequisitionId = null; i.UpdatedAt = now; i.UpdatedBy = System; audit.Log("stock", "reservation-released", null, "item", i.Id.ToString(), reason: "reservation validity lapsed", actorIdOverride: System); }
                var issued = r.Lines.Sum(l => l.QtyIssued);
                r.Status = issued > 0 ? RequisitionStatus.Issued : RequisitionStatus.Lapsed; r.LapsedAt = now;
                r.DecisionNote = ((r.DecisionNote ?? "") + $" [Reservation lapsed after {days} day(s); {reserved.Count} label(s) released]").Trim();
                audit.Log("stock", "reservation-lapsed", null, "requisition", r.Number, next: new { Released = reserved.Count, Status = r.Status.ToString() }, actorIdOverride: System);
                n += await notify.NotifyAsync(who, "NTF-04", $"Reservation lapsed — {r.Number}", $"The reservation for {r.Site!.Code} was not issued within {days} day(s). {reserved.Count} label(s) went back to available stock.{(issued > 0 ? " The requisition is closed with what was issued." : " Raise a new requisition if still needed.")}", "/requisitions", "requisition", r.Number, ct, level: 1);
            }
            else if (expires - now <= TimeSpan.FromDays(1) && r.LapseWarnedAt is null)
            {
                r.LapseWarnedAt = now;
                n += await notify.NotifyAsync(who, "NTF-04", $"Reservation lapsing tomorrow — {r.Number}", $"Material reserved for {r.Site!.Code} at {r.Store!.Code} will be released on {expires.AddHours(5):dd-MM-yyyy HH:mm} unless issued.", "/issue", "requisition", r.Number, ct, level: 0);
            }
        }
        return n;
    }

    // ---- NTF-01 escalation: a submitted requisition nobody acted on ---------------------------------
    async Task<int> ApprovalEscalationAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0;
        var cutoff = now.AddHours(-Math.Max(1, cfg.Alerts.ApprovalEscalateHours));
        var stale = await Map.Requisitions(db).Where(r => r.Status == RequisitionStatus.Submitted && (r.EscalatedAt ?? r.SubmittedAt ?? r.RaisedAt) < cutoff).ToListAsync(ct);
        foreach (var r in stale)
        {
            var role = cfg.ApprovalLevels.FirstOrDefault(l => l.Level == r.CurrentLevel)?.RoleKey ?? "storeincharge";
            var up = await notify.EscalationAsync(role, ct, r.Store);
            r.EscalatedAt = now;
            n += await notify.NotifyAsync(up.Where(u => u.Id != r.RaisedById), "NTF-01", $"Escalated: {r.Number} not approved in {cfg.Alerts.ApprovalEscalateHours} h",
                $"Requisition {r.Number} (Rs. {r.Value:N0}, {r.Site!.Code}) has waited since {(r.SubmittedAt ?? r.RaisedAt).AddHours(5):dd-MM-yyyy HH:mm} for {role}. Please act or follow up.", "/requisitions", "requisition", r.Number, ct, level: 10 + (int)((now - (r.SubmittedAt ?? r.RaisedAt)).TotalHours / cfg.Alerts.ApprovalEscalateHours));
            audit.Log("stock", "approval-escalated", null, "requisition", r.Number, next: new { From = role }, actorIdOverride: System);
        }
        return n;
    }

    // ---- NTF-06 -------------------------------------------------------------------------------------------
    async Task<int> UnacknowledgedAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0;
        var cutoff = now.AddHours(-cfg.Controls.AckOverdueHours);
        foreach (var i in await Map.Issues(db).AsNoTracking().Where(x => x.ReceivedAt == null && x.IssuedAt < cutoff).ToListAsync(ct))
        {
            var late = (now - i.IssuedAt).TotalHours > cfg.Controls.AckOverdueHours * 2;
            var who = new List<User>([.. await notify.SiteRoleAsync(i.Site!, "officer", ct), .. await notify.SiteRoleAsync(i.Site!, "supervisor", ct)]);
            if (late) who.AddRange(await notify.EscalationAsync("supervisor", ct, site: i.Site));
            n += await notify.NotifyAsync(who, "NTF-06", $"Issue {i.Number} not acknowledged at {i.Site!.Code}", $"Gate pass {i.GatePassNo} left {i.Store?.Code} on {i.IssuedAt.AddHours(5):dd-MM-yyyy HH:mm}. The officer has not confirmed receipt at site.", "/field", "issue", i.Number, ct, level: late ? 1 : 0);
        }
        return n;
    }

    // ---- NTF-07 -------------------------------------------------------------------------------------------
    async Task<int> InstallPendingAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0; var days = cfg.Alerts.InstallPendingDays;
        var items = await db.Items.AsNoTracking().Include(i => i.Site).Where(i => (i.Status == ItemStatus.Issued || i.Status == ItemStatus.AtSite) && i.IssuedAt < now.AddDays(-days) && i.SiteId != null).ToListAsync(ct);
        foreach (var g in items.GroupBy(i => i.SiteId!.Value))
        {
            var site = g.First().Site!; var oldest = g.Min(i => i.IssuedAt!.Value); var late = (now - oldest).TotalDays > days * 2;
            var who = new List<User>([.. await notify.SiteRoleAsync(site, "officer", ct), .. await notify.SiteRoleAsync(site, "supervisor", ct)]);
            if (late) who.AddRange(await notify.RoleAsync("pdoffice", ct, site: site));
            n += await notify.NotifyAsync(who, "NTF-07", $"{g.Count()} issued item(s) not installed at {site.Code}", $"Material issued since {oldest.AddHours(5):dd-MM-yyyy} has no installation evidence yet.", "/field", "site", site.Code, ct, level: late ? 1 : 0, dedup: $"NTF-07|{site.Code}|{Week(now)}|{(late ? 1 : 0)}");
        }
        return n;
    }

    // ---- FR-EXC-010 / NTF-08 escalation --------------------------------------------------------------
    async Task<int> ExceptionAgeingAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0; var days = Math.Max(1, cfg.Alerts.ExceptionEscalateDays);
        var open = await Map.Installations(db).Where(x => x.GeoStatus == GeoStatus.PendingVerification).ToListAsync(ct);
        foreach (var x in open)
        {
            var due = (x.EscalatedAt ?? x.SubmittedAt).AddDays(days);
            if (due > now || x.EscalationLevel >= 3) continue;
            x.EscalationLevel += 1; x.EscalatedAt = now;
            var who = await notify.EscalationAsync(x.EscalationLevel == 1 ? "supervisor" : "pdoffice", ct, site: x.Site);
            audit.Log("stock", "exception-escalated", null, "installation", x.Number, next: new { x.EscalationLevel, AgeDays = (int)(now - x.SubmittedAt).TotalDays }, actorIdOverride: System);
            n += await notify.NotifyAsync(who, "NTF-08", $"Escalated (level {x.EscalationLevel}): GPS exception {x.Number} unresolved", $"{x.Site!.Code}: exception raised {x.SubmittedAt.AddHours(5):dd-MM-yyyy}, {(int)(now - x.SubmittedAt).TotalDays} day(s) without a decision.", "/field?inst=" + x.Id, "installation", x.Number, ct, level: x.EscalationLevel);
        }
        return n;
    }

    // ---- NTF-09 escalation ---------------------------------------------------------------------------------
    async Task<int> RecaptureAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0;
        var cutoff = now.AddDays(-Math.Max(1, cfg.Alerts.RecaptureEscalateDays));
        var rows = await Map.Installations(db).AsNoTracking().Where(x => (x.GeoStatus == GeoStatus.Rejected || x.GeoStatus == GeoStatus.MissingEvidence) && (x.VerifiedAt ?? x.SubmittedAt) < cutoff && x.Item!.Status == ItemStatus.AtSite && x.Item.InstallationId == null && (x.Justification == null || x.Justification != "Superseded by re-captured evidence")).ToListAsync(ct);
        foreach (var x in rows.GroupBy(r => r.ItemId).Select(g => g.OrderByDescending(r => r.Id).First()))
            n += await notify.NotifyAsync(await notify.SiteRoleAsync(x.Site!, "supervisor", ct), "NTF-09", $"Re-capture still outstanding at {x.Site!.Code}", $"{x.Number}: {x.Item!.Article!.ItemCode} #{x.Item.Serial} was sent back for re-capture and {x.Officer} has not captured it again.", "/field", "installation", x.Number, ct, level: 1);
        return n;
    }

    // ---- NTF-12 -----------------------------------------------------------------------------------------------
    async Task<int> ReturnsOverdueAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0;
        var rows = await Map.Returns(db).AsNoTracking().Where(x => x.ReceivedAt == null && x.ClosedAt == null && x.ReportedAt < now.AddDays(-cfg.Controls.ReturnDays)).ToListAsync(ct);
        foreach (var x in rows)
        {
            var who = new List<User>(await notify.RoleAsync("pdoffice", ct, site: x.Site));
            if (x.Site is not null) who.AddRange(await notify.SiteRoleAsync(x.Site, "supervisor", ct));
            n += await notify.NotifyAsync(who, "NTF-12", $"Return {x.Number} overdue", $"{x.Item!.Article!.ItemCode} #{x.Item.Serial} was reported at {x.Site?.Code} on {x.ReportedAt.AddHours(5):dd-MM-yyyy} and has not reached a store.", "/returns", "return", x.Number, ct, dedup: $"NTF-12|{x.Number}|{Week(now)}");
        }
        return n;
    }

    // ---- NTF-13 escalation: adjustments and amendments waiting too long ---------------------------------
    async Task<int> PendingApprovalsAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0; var cutoff = now.AddHours(-Math.Max(1, cfg.Alerts.ApprovalEscalateHours));
        foreach (var a in await db.StockAdjustments.Include(x => x.Store).Where(x => x.Status == "Pending" && (x.EscalatedAt ?? x.RaisedAt) < cutoff).ToListAsync(ct))
        {
            a.EscalatedAt = now;
            n += await notify.NotifyAsync((await notify.EscalationAsync("storeincharge", ct, a.Store)).Where(u => u.Id != a.RaisedById), "NTF-13", $"Escalated: adjustment {a.Number} awaiting approval", $"Raised {a.RaisedAt.AddHours(5):dd-MM-yyyy HH:mm} by {a.RaisedBy} at {a.Store!.Code}; not decided in {cfg.Alerts.ApprovalEscalateHours} h.", "/adjustments", "adjustment", a.Number, ct, level: 1);
        }
        foreach (var a in await db.AmendmentRequests.AsNoTracking().Where(x => x.Status == "Pending" && x.RaisedAt < cutoff).ToListAsync(ct))
            n += await notify.NotifyAsync((await notify.RoleAsync("pdoffice", ct)).Where(u => u.Id != a.RaisedById), "NTF-13", $"Escalated: amendment {a.Number} awaiting approval", $"{a.Field} of {a.EntityType} {a.EntityRef}, requested by {a.RaisedBy} on {a.RaisedAt.AddHours(5):dd-MM-yyyy}.", "/amendments", "amendment", a.Number, ct, level: 1);
        return n;
    }

    // ---- NTF-14 daily digest ------------------------------------------------------------------------------------
    async Task<int> ReorderAsync(CancellationToken ct)
    {
        var avail = await db.Items.Where(i => i.Status == ItemStatus.Received && i.StoreLocation!.Kind == "Stock").GroupBy(i => i.ArticleId).Select(g => new { g.Key, Q = g.Sum(i => i.Qty) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var low = await db.Articles.AsNoTracking().Where(a => a.Status == "Active" && a.ReorderLevel > 0).ToListAsync(ct);
        var lines = low.Where(a => avail.GetValueOrDefault(a.Id) < a.ReorderLevel).Select(a => $"{a.ItemCode}: {avail.GetValueOrDefault(a.Id):0.###} {a.Uom} available, re-order level {a.ReorderLevel:0.###}").ToList();
        if (lines.Count == 0) return 0;
        var who = new List<User>([.. await notify.RoleAsync("storeincharge", ct), .. await notify.RoleAsync("storekeeper", ct), .. await notify.RoleAsync("procurement", ct)]);
        var day = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM-dd");
        // once a day, and again the same day only if the list of low articles changes
        var set = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(string.Join("|", low.Where(a => avail.GetValueOrDefault(a.Id) < a.ReorderLevel).Select(a => a.Id).OrderBy(x => x)))))[..10];
        return await notify.NotifyAsync(who, "NTF-14", $"{lines.Count} article(s) below re-order level", string.Join("\n", lines.Take(20)) + (lines.Count > 20 ? $"\n… and {lines.Count - 20} more" : ""), "/stock", "digest", day, ct, dedup: $"NTF-14|{day}|{set}");
    }

    // ---- NTF-15 ---------------------------------------------------------------------------------------------------
    async Task<int> GpsMismatchAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0;
        var since = now.AddDays(-cfg.Alerts.GpsMismatchDays);
        var rows = await db.Installations.AsNoTracking().Include(x => x.Site).Where(x => x.SubmittedAt >= since && (x.GeoStatus == GeoStatus.PendingVerification || x.GeoStatus == GeoStatus.ApprovedException || (x.GeoStatus == GeoStatus.Rejected && x.Justification != "Superseded by re-captured evidence"))).ToListAsync(ct);
        var bucket = (now - DateTime.UnixEpoch).Days / Math.Max(1, cfg.Alerts.GpsMismatchDays);
        foreach (var g in rows.GroupBy(x => x.OfficerId).Where(g => g.Count() >= cfg.Alerts.GpsMismatchCount))
            n += await notify.NotifyAsync(await notify.RoleAsync("pdoffice", ct), "NTF-15", $"Repeated GPS mismatch by {g.First().Officer}", $"{g.Count()} installations outside the geo-fence in {cfg.Alerts.GpsMismatchDays} days (sites {string.Join(", ", g.Select(x => x.Site!.Code).Distinct())}).", "/reports", "officer", g.Key, ct, dedup: $"NTF-15|officer|{g.Key}|{bucket}");
        foreach (var g in rows.GroupBy(x => x.SiteId).Where(g => g.Count() >= cfg.Alerts.GpsMismatchCount))
            n += await notify.NotifyAsync(await notify.RoleAsync("pdoffice", ct, site: g.First().Site), "NTF-15", $"Repeated GPS mismatch at {g.First().Site!.Code}", $"{g.Count()} installations outside the geo-fence in {cfg.Alerts.GpsMismatchDays} days. Check the site coordinates and geo-fence.", "/sites", "site", g.First().Site!.Code, ct, dedup: $"NTF-15|site|{g.Key}|{bucket}");
        return n;
    }

    // ---- NTF-16 monthly ----------------------------------------------------------------------------------------------
    async Task<int> ReturnRateAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var n = 0; var month = DateTime.UtcNow.AddHours(5).ToString("yyyy-MM");
        var failed = new[] { ItemStatus.OutOfOrder, ItemStatus.Returned, ItemStatus.UnderRepair, ItemStatus.WarrantyClaim, ItemStatus.Scrap, ItemStatus.Disposed };
        var items = await db.Items.AsNoTracking().Include(i => i.Article).ThenInclude(a => a!.Vendor).Where(i => i.InstalledAt != null || i.Status == ItemStatus.Installed).ToListAsync(ct);
        var who = new List<User>([.. await notify.RoleAsync("procurement", ct), .. await notify.RoleAsync("pdoffice", ct), .. await notify.RoleAsync("sysadmin", ct)]);
        foreach (var (key, label, grp) in items.GroupBy(i => i.ArticleId).Select(g => ("article:" + g.Key, "article " + g.First().Article!.ItemCode, g.ToList()))
            .Concat(items.GroupBy(i => i.Article!.VendorId).Select(g => ("vendor:" + g.Key, "vendor " + g.First().Article!.Vendor?.Name, g.ToList()))))
        {
            if (grp.Count < 5) continue;
            var rate = 100.0 * grp.Count(i => failed.Contains(i.Status)) / grp.Count;
            if (rate <= cfg.Alerts.ReturnRatePct) continue;
            n += await notify.NotifyAsync(who, "NTF-16", $"Unusual return rate: {label}", $"{rate:0.0}% of {grp.Count} installed have failed (threshold {cfg.Alerts.ReturnRatePct}%). See the failure analysis report.", "/reports", "failure", key, ct, dedup: $"NTF-16|{key}|{month}");
        }
        return n;
    }

    // ---- NTF-17 weekly ------------------------------------------------------------------------------------------------
    async Task<int> UnresolvedSitesAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0; var cutoff = now.AddDays(-cfg.Alerts.UnresolvedSiteDays);
        var waiting = await db.Items.AsNoTracking().Include(i => i.Site).Where(i => (i.Status == ItemStatus.Issued || i.Status == ItemStatus.AtSite) && i.SiteId != null).ToListAsync(ct);
        var lastInst = await db.Installations.AsNoTracking().GroupBy(x => x.SiteId).Select(g => new { g.Key, Last = g.Max(x => x.SubmittedAt) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        foreach (var g in waiting.GroupBy(i => i.SiteId!.Value))
        {
            var last = lastInst.TryGetValue(g.Key, out var l) ? l : g.Min(i => i.IssuedAt ?? now);
            if (last > cutoff) continue;
            var site = g.First().Site!;
            n += await notify.NotifyAsync(await notify.RoleAsync("pdoffice", ct, site: site), "NTF-17", $"Site {site.Code}: material issued, nothing installed for {cfg.Alerts.UnresolvedSiteDays}+ days", $"{g.Count()} item(s) waiting at {site.Name}. Last installation: {(lastInst.ContainsKey(g.Key) ? last.AddHours(5).ToString("dd-MM-yyyy") : "none")}.", "/map", "site", site.Code, ct, dedup: $"NTF-17|{site.Code}|{Week(now)}");
        }
        return n;
    }

    // ---- FR-NTF-003: turnaround and ageing alerts ----------------------------------------------------------------
    async Task<int> TurnaroundAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var n = 0; var cutoff = now.AddHours(-cfg.Alerts.TurnaroundHours);
        foreach (var r in await Map.Requisitions(db).AsNoTracking().Where(r => (r.Status == RequisitionStatus.Approved || r.Status == RequisitionStatus.PartIssued) && (r.SubmittedAt ?? r.RaisedAt) < cutoff).ToListAsync(ct))
            n += await notify.NotifyAsync(await notify.StoreStaffAsync(r.Store!, ct), "NTF-A1", $"Turnaround exceeded: {r.Number}", $"Submitted {(r.SubmittedAt ?? r.RaisedAt).AddHours(5):dd-MM-yyyy HH:mm}, approved but not fully issued after {cfg.Alerts.TurnaroundHours} h.", "/issue", "requisition", r.Number, ct);
        return n;
    }

    async Task<int> AgeingAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct); var now = DateTime.UtcNow; var cutoff = now.AddDays(-cfg.Controls.AgeingDays);
        var old = await db.Items.AsNoTracking().Include(i => i.StoreLocation).ThenInclude(l => l!.Store).Include(i => i.Article).Where(i => i.Status == ItemStatus.Received && i.StoreLocation!.Kind == "Stock" && i.ReceivedAt < cutoff).ToListAsync(ct);
        var n = 0;
        foreach (var g in old.GroupBy(i => i.StoreLocation!.StoreId))
        {
            var store = g.First().StoreLocation!.Store!;
            var lines = g.GroupBy(i => i.Article!.ItemCode).Select(a => $"{a.Key}: {a.Sum(i => i.Qty):0.###}").Take(15);
            n += await notify.NotifyAsync(await notify.RoleAsync("storeincharge", ct, store), "NTF-A2", $"{g.Count()} label(s) in {store.Code} older than {cfg.Controls.AgeingDays} days", string.Join("\n", lines), "/reports", "store", store.Code, ct, dedup: $"NTF-A2|{store.Code}|{Week(now)}");
        }
        return n;
    }

    // ---- FR-NTF-002: e-mail and SMS copies ---------------------------------------------------------------------------
    async Task<int> DeliverAsync(CancellationToken ct)
    {
        var ch = await channels.GetAsync(ct); var n = 0;
        var pending = await db.Notifications.Where(x => x.EmailStatus == "Pending" || x.SmsStatus == "Pending").OrderBy(x => x.Id).Take(50).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var users = await db.Users.AsNoTracking().Where(u => pending.Select(p => p.UserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        foreach (var x in pending)
        {
            var u = users.GetValueOrDefault(x.UserId);
            if (x.EmailStatus == "Pending")
            {
                if (!ch.EmailEnabled || string.IsNullOrWhiteSpace(u?.Email)) x.EmailStatus = "Skipped";
                else try
                    {
                        var link = string.IsNullOrWhiteSpace(ch.PortalUrl) || x.Link is null ? "" : $"\n\nOpen: {ch.PortalUrl!.TrimEnd('/')}{x.Link}";
                        await sender.SendEmailAsync(ch, [u.Email!], $"[SLIMS] {x.Title}", $"{x.Body}{link}\n\n— SLIMS, {x.Code}", null, ct); x.EmailStatus = "Sent"; n++;
                    }
                    catch (Exception e) { x.EmailStatus = "Failed"; x.DeliveryError = Trunc(e.Message, 400); }
            }
            if (x.SmsStatus == "Pending")
            {
                if (!ch.SmsEnabled || string.IsNullOrWhiteSpace(u?.Phone)) x.SmsStatus = "Skipped";
                else try { await sender.SendSmsAsync(ch, u.Phone!, Trunc($"SLIMS: {x.Title}", 160), ct); x.SmsStatus = "Sent"; n++; }
                    catch (Exception e) { x.SmsStatus = "Failed"; x.DeliveryError = Trunc(e.Message, 400); }
            }
        }
        return n;
    }
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
