using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>Role-aware dashboards (BRD 7, FR-021). Every count is taken from the same permission-filtered queries the
/// lists use, so a card always reconciles with the list it drills into.</summary>
public class DashboardService(IAppDbContext db, UserContext users, HierarchyService h, TaskService tasks, DocumentService documents, ConfigService configs)
{
    static readonly WorkTaskStatus[] Open = [WorkTaskStatus.Assigned, WorkTaskStatus.InProgress, WorkTaskStatus.Blocked, WorkTaskStatus.SubmittedForReview, WorkTaskStatus.Returned];

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var cfg = await configs.GetAsync(ct);
        var now = DateTime.UtcNow; var today = now.Date;
        var all = await h.UsersAsync(ct);
        string N(string? id) => id is null ? "—" : all.GetValueOrDefault(id)?.Name ?? id;

        var visibleTasks = await (await tasks.VisibleQueryAsync(me, ct)).ToListAsync(ct);
        var visibleDocs = await (await documents.VisibleQueryAsync(me, ct)).Include(d => d.Route).ToListAsync(ct);
        var taskP = await h.PrincipalsAsync(me.Id, DelegationScope.Tasks, null, ct);
        var anyP = await h.PrincipalsAsync(me.Id, DelegationScope.All, null, ct);
        foreach (var dl in await h.DelegationsAsync(ct)) if (dl.ToUserId == me.Id && dl.CoversDate(now)) anyP.Add(dl.FromUserId);
        var subs = new HashSet<string>(); foreach (var p in anyP) subs.UnionWith(await h.SubordinatesAsync(p, ct));
        var scope = AccessPolicy.Can(me, Permissions.Audit) ? "All records (audit access)" : subs.Count > 0 ? $"You and {subs.Count} officer(s) in your reporting line" : "Your own work";

        // ---- my work queue -----------------------------------------------------------------------------------
        var queue = new List<QueueItemDto>();
        foreach (var t in visibleTasks.Where(t => t.OwnerId != null && taskP.Contains(t.OwnerId) && t.Status is WorkTaskStatus.Assigned or WorkTaskStatus.InProgress or WorkTaskStatus.Blocked or WorkTaskStatus.Returned))
            queue.Add(new QueueItemDto("task", t.Id, t.Number, t.Title, t.Status switch { WorkTaskStatus.Assigned => "Acknowledge and start", WorkTaskStatus.Returned => "Revise and resubmit", WorkTaskStatus.Blocked => "Blocked — update", _ => "Update progress" },
                t.Priority.ToString(), t.DueOn, t.AssignedAt, t.IsOverdue(now), t.OwnerId == me.Id ? null : N(t.OwnerId)));
        foreach (var t in visibleTasks.Where(t => t.Status == WorkTaskStatus.SubmittedForReview && taskP.Contains(t.ClosureAuthorityId ?? t.AssignedById)))
            queue.Add(new QueueItemDto("task", t.Id, t.Number, t.Title, "Review output and close", t.Priority.ToString(), t.DueOn, t.SubmittedAt, t.IsOverdue(now), (t.ClosureAuthorityId ?? t.AssignedById) == me.Id ? null : N(t.ClosureAuthorityId ?? t.AssignedById)));
        foreach (var d in visibleDocs)
        {
            foreach (var s in d.Route.Where(s => s.Status == StageStatus.Active && s.UserId != null && anyP.Contains(s.UserId) && (d.Status == DocumentStatus.InRoute || s.AdHoc)))
                queue.Add(new QueueItemDto("document", d.Id, d.Number, d.Subject, StageRules.Requested(s.Type), null, s.DueAt, s.ReceivedAt, s.DueAt < now, s.UserId == me.Id ? null : N(s.UserId)));
            if (d.Status == DocumentStatus.Returned && anyP.Contains(d.InitiatorId))
                queue.Add(new QueueItemDto("document", d.Id, d.Number, d.Subject, "Revise and resubmit", null, null, d.Route.Where(r => r.Status == StageStatus.Returned).Max(r => r.ActedAt), false, d.InitiatorId == me.Id ? null : N(d.InitiatorId)));
            if (d.Status == DocumentStatus.Draft && anyP.Contains(d.InitiatorId))
                queue.Add(new QueueItemDto("document", d.Id, d.Number, d.Subject, "Draft — complete and submit", null, null, d.CreatedAt, false, d.InitiatorId == me.Id ? null : N(d.InitiatorId)));
        }
        queue = queue.OrderByDescending(q => q.Overdue).ThenBy(q => q.DueAt ?? DateTime.MaxValue).ThenBy(q => q.ReceivedAt).ToList();
        var unread = await db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct);

        var myOpen = visibleTasks.Where(t => t.OwnerId != null && taskP.Contains(t.OwnerId) && Open.Contains(t.Status)).ToList();
        var myCounts = new List<CountDto>
        {
            new("my-open", "My open tasks", myOpen.Count),
            new("my-overdue", "My overdue tasks", myOpen.Count(t => t.IsOverdue(now))),
            new("my-review", "Awaiting my review", queue.Count(q => q.Action == "Review output and close")),
            new("my-docs", "Documents awaiting my action", queue.Count(q => q.Kind == "document" && q.Action != "Revise and resubmit" && !q.Action.StartsWith("Draft"))),
            new("my-sign", "Signatures awaiting me", queue.Count(q => q.Action == "Signature")),
            new("my-returned", "Returned to me", queue.Count(q => q.Action == "Revise and resubmit")),
            new("unread", "Unread notifications", unread),
        };

        // ---- tasks across the portfolio --------------------------------------------------------------------------
        var taskStatus = Enum.GetValues<WorkTaskStatus>().Select(s => new CountDto(s.ToString(), TaskService.Label(s), visibleTasks.Count(t => t.Status == s))).ToList();
        var open = visibleTasks.Where(t => Open.Contains(t.Status)).ToList();
        var overdue = open.Where(t => t.IsOverdue(now)).ToList();
        var taskRisk = new List<CountDto>
        {
            new("open", "Open", open.Count),
            new("overdue", "Overdue", overdue.Count),
            new("due-soon", $"Due in {cfg.Alerts.DueSoonDays} days", open.Count(t => t.DueOn is DateTime d && d.Date >= today && d.Date <= today.AddDays(cfg.Alerts.DueSoonDays))),
            new("blocked", "Blocked", open.Count(t => t.Status == WorkTaskStatus.Blocked)),
            new("escalated", "Escalated", open.Count(t => t.EscalationLevel > 0)),
            new("review", "Awaiting closure review", open.Count(t => t.Status == WorkTaskStatus.SubmittedForReview)),
        };
        int Age(WorkTask t) => (int)(today - t.DueOn!.Value.Date).TotalDays;
        var aging = new List<CountDto>
        {
            new("1-7", "1–7 days", overdue.Count(t => Age(t) <= 7)), new("8-30", "8–30 days", overdue.Count(t => Age(t) is > 7 and <= 30)),
            new("31-90", "31–90 days", overdue.Count(t => Age(t) is > 30 and <= 90)), new("90+", "Over 90 days", overdue.Count(t => Age(t) > 90)),
        };
        var ownerLoad = visibleTasks.Where(t => t.OwnerId != null && t.Status != WorkTaskStatus.Draft).GroupBy(t => t.OwnerId!)
            .Select(g => new OwnerLoadDto(g.Key, N(g.Key), all.GetValueOrDefault(g.Key)?.Designation?.Title, g.Count(t => Open.Contains(t.Status)), g.Count(t => t.IsOverdue(now)),
                g.Count(t => t.Status == WorkTaskStatus.Blocked), g.Count(t => t.Status == WorkTaskStatus.SubmittedForReview), g.Count(t => t.Status == WorkTaskStatus.Completed)))
            .OrderByDescending(o => o.Overdue).ThenByDescending(o => o.Open).Take(15).ToList();

        // ---- documents --------------------------------------------------------------------------------------------
        var docStatus = Enum.GetValues<DocumentStatus>().Select(s => new CountDto(s.ToString(), s == DocumentStatus.InRoute ? "In route" : s.ToString(), visibleDocs.Count(d => d.Status == s))).ToList();
        var activeStages = visibleDocs.Where(d => d.Status == DocumentStatus.InRoute).SelectMany(d => d.Route.Where(s => s.Status == StageStatus.Active && s.Blocking)).ToList();
        var docRisk = new List<CountDto>
        {
            new("approval", "Awaiting approval / concurrence", visibleDocs.Count(d => d.Status == DocumentStatus.InRoute && d.Route.Any(s => s.Status == StageStatus.Active && s.Type is StageType.Approval or StageType.Concurrence))),
            new("signature", "Awaiting signature", visibleDocs.Count(d => d.Status == DocumentStatus.InRoute && d.Route.Any(s => s.Status == StageStatus.Active && s.Type == StageType.Signature))),
            new("overdue", "Overdue at a stage", visibleDocs.Count(d => d.Status == DocumentStatus.InRoute && d.Route.Any(s => s.Status == StageStatus.Active && s.Blocking && s.DueAt < now))),
            new("returned", "Returned for revision", visibleDocs.Count(d => d.Status == DocumentStatus.Returned)),
        };
        var bottlenecks = activeStages.Where(s => s.UserId != null).GroupBy(s => s.UserId!)
            .Select(g => new HolderLoadDto(g.Key, N(g.Key), all.GetValueOrDefault(g.Key)?.Designation?.Title, g.Count(), Math.Round(g.Max(s => (now - (s.ReceivedAt ?? now)).TotalHours), 1), g.Count(s => s.DueAt < now)))
            .OrderByDescending(x => x.OldestHours).Take(10).ToList();
        var since = now.AddDays(-180);
        var turnaround = visibleDocs.SelectMany(d => d.Route).Where(s => s.Status == StageStatus.Done && s.ReceivedAt != null && s.ActedAt != null && s.ActedAt >= since && !s.AdHoc)
            .GroupBy(s => s.Type).Select(g => new StageTurnaroundDto(g.Key.ToString(), g.Count(), Math.Round(g.Average(s => (s.ActedAt!.Value - s.ReceivedAt!.Value).TotalHours), 1), Math.Round(g.Max(s => (s.ActedAt!.Value - s.ReceivedAt!.Value).TotalHours), 1)))
            .OrderBy(x => x.StageType).ToList();
        var blocked = await tasks.ToDtosAsync(open.Where(t => t.Status == WorkTaskStatus.Blocked).OrderBy(t => t.BlockedSince).Take(10).ToList(), ct);

        return new DashboardDto(now, scope, queue.Take(40).ToList(), myCounts, taskStatus, taskRisk, aging, ownerLoad, docStatus, docRisk, bottlenecks, turnaround, blocked,
            visibleDocs.Count(d => d.Status == DocumentStatus.Returned), visibleDocs.Sum(d => d.ReturnCount));
    }
}

/// <summary>Standard reports (FR-026). Every result carries the report date, who generated it and the filters applied,
/// and the same rows go to the screen, CSV and Excel.</summary>
public class ReportService(IAppDbContext db, UserContext users, HierarchyService h, TaskService tasks, DocumentService documents, AuditService audit, IReportWriter writer)
{
    public static readonly List<ReportInfo> Catalog =
    [
        new("task-aging", "Task aging", "Open tasks with due date, days overdue, owner, status and progress.", ["from", "to", "user", "unit", "status"]),
        new("workload", "Workload by officer", "Open, overdue, blocked and completed tasks per owner.", ["unit"]),
        new("turnaround", "Stage turnaround", "Time held at each completed document stage, by stage type and officer, against the stage target.", ["from", "to", "type", "user"]),
        new("movement", "Document movement register", "Every forward, return, decision and amendment, with time held.", ["from", "to", "type", "user"]),
        new("pending-approvals", "Pending approvals and signatures", "Documents now waiting at an approval, concurrence or signature stage.", ["type", "user"]),
        new("signatures", "Signature register", "Signature fields, signers, codes, the version signed and status.", ["from", "to", "type", "user"]),
        new("escalations", "Reminders and escalations", "Every reminder and escalation the system sent, with recipients.", ["from", "to"]),
        new("audit", "Audit trail", "Actions, refusals, route changes, approvals, downloads and signatures (auditors).", ["from", "to", "user", "kind"]),
    ];

    public async Task<ReportResult> RunAsync(string key, ReportQuery f, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Report, ct);
        if (key == "audit" && !AccessPolicy.Can(me, Permissions.Audit)) throw AppException.Forbidden("The audit trail report is for auditors.", "permission");
        var info = Catalog.FirstOrDefault(c => c.Key == key) ?? throw AppException.NotFound("Report");
        var all = await h.UsersAsync(ct);
        string N(string? id) => id is null ? "" : all.GetValueOrDefault(id)?.Name ?? id;
        var now = DateTime.UtcNow;
        var filters = new Dictionary<string, string>();
        if (f.From is DateTime fr) filters["From"] = fr.ToString("dd-MM-yyyy");
        if (f.To is DateTime to) filters["To"] = to.ToString("dd-MM-yyyy");
        if (!string.IsNullOrEmpty(f.UserId)) filters["Officer"] = N(f.UserId);
        if (f.OrgUnitId is int u) filters["Unit"] = (await db.OrgUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == u, ct))?.Name ?? u.ToString();
        if (!string.IsNullOrEmpty(f.Status)) filters["Status"] = f.Status;
        if (f.DocumentTypeId is int dt) filters["Document type"] = (await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dt, ct))?.Name ?? dt.ToString();
        if (!string.IsNullOrEmpty(f.Kind)) filters["Kind"] = f.Kind;
        if (filters.Count == 0) filters["Filters"] = "none";
        var end = f.To?.Date.AddDays(1);
        var cols = new List<ReportColumn>();
        var rows = new List<Dictionary<string, object?>>();
        void C(string k, string l, string kind = "text") => cols.Add(new(k, l, kind));

        switch (key)
        {
            case "task-aging":
            {
                C("number", "Task"); C("title", "Title"); C("owner", "Owner"); C("assignedBy", "Assigned by"); C("status", "Status"); C("priority", "Priority"); C("due", "Due", "date");
                C("daysOverdue", "Days overdue", "number"); C("progress", "Progress %", "number"); C("blocker", "Blocker"); C("escalation", "Escalation level", "number");
                var q = (await tasks.VisibleQueryAsync(me, ct)).Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled && t.Status != WorkTaskStatus.Draft);
                if (f.From is DateTime a) q = q.Where(t => t.DueOn >= a.Date);
                if (end is DateTime e) q = q.Where(t => t.DueOn < e);
                if (!string.IsNullOrEmpty(f.UserId)) q = q.Where(t => t.OwnerId == f.UserId);
                if (f.OrgUnitId is int unit) q = q.Where(t => t.OrgUnitId == unit);
                if (!string.IsNullOrEmpty(f.Status) && Enum.TryParse<WorkTaskStatus>(f.Status, out var st)) q = q.Where(t => t.Status == st);
                foreach (var t in (await q.ToListAsync(ct)).OrderByDescending(t => t.IsOverdue(now) ? (now.Date - t.DueOn!.Value.Date).TotalDays : -1))
                    rows.Add(new() { ["number"] = t.Number, ["title"] = t.Title, ["owner"] = N(t.OwnerId), ["assignedBy"] = N(t.AssignedById), ["status"] = TaskService.Label(t.Status), ["priority"] = t.Priority.ToString(),
                        ["due"] = t.DueOn, ["daysOverdue"] = t.IsOverdue(now) ? (int)(now.Date - t.DueOn!.Value.Date).TotalDays : 0, ["progress"] = t.Progress, ["blocker"] = t.Blocker, ["escalation"] = t.EscalationLevel });
                break;
            }
            case "workload":
            {
                C("owner", "Officer"); C("designation", "Designation"); C("open", "Open", "number"); C("overdue", "Overdue", "number"); C("blocked", "Blocked", "number"); C("review", "In review", "number"); C("completed", "Completed", "number"); C("returned", "Times returned", "number");
                var q = (await tasks.VisibleQueryAsync(me, ct)).Where(t => t.OwnerId != null && t.Status != WorkTaskStatus.Draft);
                if (f.OrgUnitId is int unit) q = q.Where(t => t.OrgUnitId == unit);
                foreach (var g in (await q.ToListAsync(ct)).GroupBy(t => t.OwnerId!).OrderBy(g => N(g.Key)))
                    rows.Add(new() { ["owner"] = N(g.Key), ["designation"] = all.GetValueOrDefault(g.Key)?.Designation?.Title, ["open"] = g.Count(t => t.IsOpen),
                        ["overdue"] = g.Count(t => t.IsOverdue(now)), ["blocked"] = g.Count(t => t.Status == WorkTaskStatus.Blocked), ["review"] = g.Count(t => t.Status == WorkTaskStatus.SubmittedForReview),
                        ["completed"] = g.Count(t => t.Status == WorkTaskStatus.Completed), ["returned"] = g.Sum(t => t.ReturnCount) });
                break;
            }
            case "turnaround":
            {
                C("stageType", "Stage type"); C("officer", "Officer"); C("completed", "Completed", "number"); C("avgHours", "Average hours", "number"); C("maxHours", "Longest hours", "number"); C("withinTarget", "Within target %", "number");
                var docs = await VisibleDocsAsync(me, f, ct);
                var stages = docs.SelectMany(d => d.Route.Select(s => (d, s))).Where(x => x.s.Status == StageStatus.Done && x.s.ReceivedAt != null && x.s.ActedAt != null && !x.s.AdHoc).ToList();
                if (f.From is DateTime a) stages = stages.Where(x => x.s.ActedAt >= a.Date).ToList();
                if (end is DateTime e) stages = stages.Where(x => x.s.ActedAt < e).ToList();
                if (!string.IsNullOrEmpty(f.UserId)) stages = stages.Where(x => x.s.UserId == f.UserId).ToList();
                foreach (var g in stages.GroupBy(x => (x.s.Type, x.s.UserId)).OrderBy(g => g.Key.Type).ThenBy(g => N(g.Key.UserId)))
                {
                    var hours = g.Select(x => (x.s.ActedAt!.Value - x.s.ReceivedAt!.Value).TotalHours).ToList();
                    var within = g.Count(x => x.s.DueAt is null || x.s.ActedAt <= x.s.DueAt);
                    rows.Add(new() { ["stageType"] = g.Key.Type.ToString(), ["officer"] = N(g.Key.UserId), ["completed"] = g.Count(), ["avgHours"] = Math.Round(hours.Average(), 1), ["maxHours"] = Math.Round(hours.Max(), 1), ["withinTarget"] = Math.Round(100.0 * within / g.Count(), 1) });
                }
                break;
            }
            case "movement":
            {
                C("at", "When", "datetime"); C("document", "Document"); C("subject", "Subject"); C("action", "Action"); C("stage", "Stage"); C("from", "From"); C("to", "To"); C("actingFor", "Acting for"); C("version", "Version", "number"); C("held", "Hours held", "number"); C("comment", "Comment");
                var docs = (await VisibleDocsAsync(me, f, ct)).ToDictionary(d => d.Id);
                var ids = docs.Keys.ToList();
                var q = db.Movements.AsNoTracking().Where(m => ids.Contains(m.DocumentId));
                if (f.From is DateTime a) q = q.Where(m => m.At >= a.Date);
                if (end is DateTime e) q = q.Where(m => m.At < e);
                if (!string.IsNullOrEmpty(f.UserId)) q = q.Where(m => m.FromUserId == f.UserId || m.ToUserId == f.UserId);
                foreach (var m in await q.OrderBy(m => m.Id).Take(20000).ToListAsync(ct))
                    rows.Add(new() { ["at"] = m.At, ["document"] = docs[m.DocumentId].Number, ["subject"] = docs[m.DocumentId].Subject, ["action"] = m.Action, ["stage"] = m.StageName, ["from"] = N(m.FromUserId), ["to"] = N(m.ToUserId),
                        ["actingFor"] = N(m.ActingForId), ["version"] = m.VersionNo, ["held"] = m.HeldHours is double hh ? Math.Round(hh, 1) : null, ["comment"] = m.Comment });
                break;
            }
            case "pending-approvals":
            {
                C("document", "Document"); C("subject", "Subject"); C("type", "Document type"); C("stage", "Stage"); C("stageType", "Stage type"); C("officer", "Officer"); C("designation", "Designation");
                C("received", "Received", "datetime"); C("due", "Due", "datetime"); C("held", "Hours held", "number"); C("overdue", "Overdue");
                var docs = await VisibleDocsAsync(me, f, ct);
                foreach (var (d, s) in docs.Where(d => d.Status == DocumentStatus.InRoute).SelectMany(d => d.Route.Where(s => s.Status == StageStatus.Active && s.Type is StageType.Approval or StageType.Concurrence or StageType.Signature).Select(s => (d, s)))
                    .Where(x => string.IsNullOrEmpty(f.UserId) || x.s.UserId == f.UserId).OrderBy(x => x.s.ReceivedAt))
                    rows.Add(new() { ["document"] = d.Number, ["subject"] = d.Subject, ["type"] = d.DocumentType?.Name, ["stage"] = s.Name, ["stageType"] = s.Type.ToString(), ["officer"] = N(s.UserId), ["designation"] = all.GetValueOrDefault(s.UserId ?? "")?.Designation?.Title,
                        ["received"] = s.ReceivedAt, ["due"] = s.DueAt, ["held"] = s.ReceivedAt is DateTime r ? Math.Round((now - r).TotalHours, 1) : null, ["overdue"] = s.DueAt < now ? "Yes" : "No" });
                break;
            }
            case "signatures":
            {
                C("document", "Document"); C("subject", "Subject"); C("signer", "Designated signer"); C("signedBy", "Signed by"); C("actingFor", "Acting for"); C("status", "Status"); C("signedAt", "Signed", "datetime");
                C("code", "Signature code"); C("hash", "Document SHA-256"); C("method", "Authentication"); C("page", "Page", "number"); C("note", "Note");
                var docs = await VisibleDocsAsync(me, f, ct, withFields: true);
                foreach (var (d, s) in docs.SelectMany(d => d.SignatureFields.Select(s => (d, s))).Where(x => string.IsNullOrEmpty(f.UserId) || x.s.SignerUserId == f.UserId || x.s.SignedById == f.UserId)
                    .Where(x => f.From is null || x.s.SignedAt >= f.From.Value.Date || x.s.SignedAt == null).Where(x => end is null || x.s.SignedAt < end || x.s.SignedAt == null).OrderBy(x => x.d.Id).ThenBy(x => x.s.Order))
                    rows.Add(new() { ["document"] = d.Number, ["subject"] = d.Subject, ["signer"] = N(s.SignerUserId), ["signedBy"] = N(s.SignedById), ["actingFor"] = N(s.ActingForId), ["status"] = s.Status.ToString(), ["signedAt"] = s.SignedAt,
                        ["code"] = s.SignatureCode, ["hash"] = s.DocumentHash, ["method"] = s.AuthMethod, ["page"] = s.Page, ["note"] = s.InvalidatedReason });
                break;
            }
            case "escalations":
            {
                C("at", "When", "datetime"); C("reference", "Reference"); C("kind", "Item"); C("level", "Level"); C("recipients", "Recipients"); C("reason", "Reason");
                var q = db.EscalationLogs.AsNoTracking().AsQueryable();
                if (f.From is DateTime a) q = q.Where(x => x.At >= a.Date);
                if (end is DateTime e) q = q.Where(x => x.At < e);
                var visibleTaskIds = (await (await tasks.VisibleQueryAsync(me, ct)).Select(t => t.Id).ToListAsync(ct)).Select(i => i.ToString()).ToHashSet();
                var visibleStageIds = (await VisibleDocsAsync(me, new ReportQuery(null, null, null, null, null, null, null), ct)).SelectMany(d => d.Route).Select(s => s.Id.ToString()).ToHashSet();
                foreach (var x in (await q.OrderByDescending(x => x.Id).Take(5000).ToListAsync(ct)).Where(x => x.EntityType == "task" ? visibleTaskIds.Contains(x.EntityId) : visibleStageIds.Contains(x.EntityId)))
                    rows.Add(new() { ["at"] = x.At, ["reference"] = x.Reference, ["kind"] = x.EntityType == "task" ? "Task" : "Document stage", ["level"] = x.Level == 0 ? "Reminder" : $"Escalation {x.Level}",
                        ["recipients"] = string.Join(", ", x.Recipients.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(N)), ["reason"] = x.Reason });
                break;
            }
            case "audit":
            {
                C("at", "When", "datetime"); C("kind", "Kind"); C("action", "Action"); C("result", "Result"); C("actor", "Actor"); C("actingFor", "Acting for"); C("entity", "Record"); C("reason", "Reason"); C("ip", "IP"); C("correlation", "Correlation");
                foreach (var a in await audit.ListAsync(f.Kind, null, null, null, null, null, f.From, f.To, 5000, ct, string.IsNullOrEmpty(f.UserId) ? null : f.UserId))
                    rows.Add(new() { ["at"] = a.At, ["kind"] = a.Kind, ["action"] = a.Action, ["result"] = a.Result, ["actor"] = a.ActorName ?? a.ActorId, ["actingFor"] = N(a.ActingForId), ["entity"] = $"{a.EntityType} {a.EntityId}".Trim(),
                        ["reason"] = a.Reason, ["ip"] = a.Ip, ["correlation"] = a.CorrelationId });
                break;
            }
        }
        audit.Log("report", "report-run", me, "report", key, next: filters);
        await db.SaveChangesAsync(ct);
        return new ReportResult(key, info.Title, now, me.Name, filters, cols, rows);
    }

    async Task<List<DocumentCase>> VisibleDocsAsync(User me, ReportQuery f, CancellationToken ct, bool withFields = false)
    {
        IQueryable<DocumentCase> q = (await documents.VisibleQueryAsync(me, ct)).Include(d => d.Route).Include(d => d.DocumentType);
        if (withFields) q = q.Include(d => d.SignatureFields);
        if (f.DocumentTypeId is int t) q = q.Where(d => d.DocumentTypeId == t);
        return await q.ToListAsync(ct);
    }

    public async Task<(byte[] Bytes, string Name, string Type)> ExportAsync(string key, ReportQuery f, string format, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Export, ct);
        var r = await RunAsync(key, f, ct);
        audit.Log("report", "report-exported", me, "report", key, next: new { format, r.Filters, Rows = r.Rows.Count });
        await db.SaveChangesAsync(ct);
        var stamp = r.GeneratedAt.ToString("yyyyMMdd-HHmm");
        return format == "xlsx"
            ? (writer.Xlsx(r), $"PERA-{key}-{stamp}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            : (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Csv(r))).ToArray(), $"PERA-{key}-{stamp}.csv", "text/csv");
    }

    public static string Csv(ReportResult r)
    {
        static string E(object? v)
        {
            var s = v switch { null => "", DateTime d => d.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture), double x => x.ToString(CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };
            // keep spreadsheet formulas out of exported cells
            if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
            return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Report,{E(r.Title)}");
        sb.AppendLine($"Generated,{E(r.GeneratedAt.ToString("dd-MM-yyyy HH:mm") + " UTC")} by {E(r.GeneratedBy)}");
        sb.AppendLine($"Filters,{E(string.Join("; ", r.Filters.Select(kv => $"{kv.Key}: {kv.Value}")))}");
        sb.AppendLine($"Rows,{r.Rows.Count}");
        sb.AppendLine();
        sb.AppendLine(string.Join(',', r.Columns.Select(c => E(c.Label))));
        foreach (var row in r.Rows) sb.AppendLine(string.Join(',', r.Columns.Select(c => E(row.GetValueOrDefault(c.Key)))));
        return sb.ToString();
    }
}

/// <summary>Reminders before due dates and escalation up the hierarchy after them (BRD 8, FR-024). Runs from the
/// background worker; each item is reminded once and escalated at most once per level, and every event is logged.</summary>
public class AutomationService(IAppDbContext db, HierarchyService h, ConfigService configs, NotificationService notify, AuditService audit)
{
    public record RunResult(int Reminders, int Escalations, List<string> Errors);

    public async Task<RunResult> RunAsync(CancellationToken ct, DateTime? asOf = null)
    {
        var now = asOf ?? DateTime.UtcNow;
        var cfg = (await configs.GetAsync(ct)).Alerts;
        var all = await h.UsersAsync(ct);
        int reminders = 0, escalations = 0; var errors = new List<string>();
        string N(string? id) => id is null ? "—" : all.GetValueOrDefault(id)?.Name ?? id;

        var open = await db.Tasks.Where(t => t.DueOn != null && (t.Status == WorkTaskStatus.Assigned || t.Status == WorkTaskStatus.InProgress || t.Status == WorkTaskStatus.Blocked || t.Status == WorkTaskStatus.Returned)).ToListAsync(ct);
        foreach (var t in open)
        {
            try
            {
                var dueEnd = t.DueOn!.Value.Date.AddDays(1);          // due at the end of the due date
                if (t.RemindedAt is null && now < dueEnd && now >= dueEnd.AddHours(-cfg.ReminderHours))
                {
                    t.RemindedAt = now; reminders++;
                    Log("task", t.Id.ToString(), t.Number, 0, [t.OwnerId], $"Due {t.DueOn:dd-MM-yyyy}", now);
                    notify.Notify([t.OwnerId], "deadline", $"Due soon: {t.Number}", $"{t.Title} is due {t.DueOn:dd-MM-yyyy}.", "task", t.Id.ToString());
                }
                var level = now >= dueEnd.AddHours(cfg.Escalate2Hours) ? 2 : now >= dueEnd.AddHours(cfg.Escalate1Hours) ? 1 : 0;
                if (level > t.EscalationLevel && t.OwnerId is not null)
                {
                    var chain = await h.ChainAboveAsync(t.OwnerId, ct);
                    var to = level == 1 ? new List<string?> { chain.FirstOrDefault(), t.AssignedById } : [chain.Skip(1).FirstOrDefault() ?? chain.FirstOrDefault(), t.AssignedById];
                    var days = (int)Math.Floor((now - dueEnd).TotalDays) + 1;
                    t.EscalationLevel = level; escalations++;
                    var reason = $"Overdue by {days} day(s); owner {N(t.OwnerId)}";
                    Log("task", t.Id.ToString(), t.Number, level, to, reason, now);
                    notify.Notify(to, "escalation", $"Escalation {level}: {t.Number} overdue", $"{t.Title} — {reason}.", "task", t.Id.ToString());
                    notify.Notify([t.OwnerId], "escalation", $"Overdue and escalated: {t.Number}", $"Escalated to {string.Join(", ", to.Where(x => x != null).Distinct().Select(N))}.", "task", t.Id.ToString());
                    db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = "system", Event = "escalated", Stage = t.Status, NewValue = $"Level {level}", Comment = $"{reason}. Notified {string.Join(", ", to.Where(x => x != null).Distinct().Select(N))}." });
                }
            }
            catch (Exception e) { errors.Add($"{t.Number}: {e.Message}"); }
        }

        var stages = await db.RouteStages.Where(s => s.Status == StageStatus.Active && s.DueAt != null && !s.AdHoc && s.Type != StageType.Information).ToListAsync(ct);
        var docIds = stages.Select(s => s.DocumentId).Distinct().ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id) && d.Status == DocumentStatus.InRoute).ToDictionaryAsync(d => d.Id, ct);
        foreach (var s in stages.Where(s => docs.ContainsKey(s.DocumentId)))
        {
            try
            {
                var d = docs[s.DocumentId];
                var due = s.DueAt!.Value;
                if (s.RemindedAt is null && now < due && now >= due.AddHours(-cfg.ReminderHours))
                {
                    s.RemindedAt = now; reminders++;
                    Log("stage", s.Id.ToString(), $"{d.Number} · {s.Name}", 0, [s.UserId], $"Due {due:dd-MM-yyyy HH:mm} UTC", now);
                    notify.Notify([s.UserId], "deadline", $"Due soon: {d.Number}", $"{StageRules.Requested(s.Type)} on \"{d.Subject}\" is due {due:dd-MM-yyyy HH:mm} UTC.", "document", d.Id.ToString());
                }
                var level = now >= due.AddHours(cfg.Escalate2Hours) ? 2 : now >= due.AddHours(cfg.Escalate1Hours) ? 1 : 0;
                if (level > s.EscalationLevel && s.UserId is not null)
                {
                    var chain = await h.ChainAboveAsync(s.UserId, ct);
                    var to = new List<string?> { level == 1 ? chain.FirstOrDefault() : chain.Skip(1).FirstOrDefault() ?? chain.FirstOrDefault(), d.InitiatorId };
                    s.EscalationLevel = level; escalations++;
                    var reason = $"{s.Name} with {N(s.UserId)} for {(now - (s.ReceivedAt ?? now)).TotalHours:0} h, {(now - due).TotalHours:0} h past target";
                    Log("stage", s.Id.ToString(), $"{d.Number} · {s.Name}", level, to, reason, now);
                    notify.Notify(to, "escalation", $"Escalation {level}: {d.Number} held at {s.Name}", reason, "document", d.Id.ToString());
                    notify.Notify([s.UserId], "escalation", $"Overdue and escalated: {d.Number}", reason, "document", d.Id.ToString());
                    db.Movements.Add(new Movement { DocumentId = d.Id, At = now, ToUserId = to.FirstOrDefault(), RouteStageId = s.Id, StageName = s.Name, Action = $"Escalated (level {level})", Comment = reason });
                }
            }
            catch (Exception e) { errors.Add($"stage {s.Id}: {e.Message}"); }
        }
        await db.SaveChangesAsync(ct);
        return new RunResult(reminders, escalations, errors);
    }

    void Log(string type, string id, string reference, int level, IEnumerable<string?> to, string reason, DateTime now)
    {
        var recipients = string.Join(',', to.Where(x => x != null).Distinct());
        db.EscalationLogs.Add(new EscalationLog { At = now, EntityType = type, EntityId = id, Reference = reference, Level = level, Recipients = recipients, Reason = reason });
        audit.Log("escalation", level == 0 ? "reminder-sent" : $"escalated-level-{level}", null, type, id, next: new { reference, recipients }, reason: reason, actorIdOverride: "system");
    }
}
