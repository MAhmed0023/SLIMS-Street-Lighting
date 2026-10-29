using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>Task creation, delegation down the hierarchy, progress, blockers, review and closure (FR-003 … FR-008).
/// Every action is checked here against the caller's place in the task: owner, assigning officer, closure authority
/// or supervisor of the owner — directly or as an acting officer.</summary>
public partial class TaskService(IAppDbContext db, UserContext users, HierarchyService h, AuditService audit, NotificationService notify,
    NumberingService numbers, FileService files, ConfigService configs)
{
    public static readonly string[] ActionNames = ["edit", "assign", "start", "progress", "block", "unblock", "submit", "close", "return", "reopen", "cancel", "reassign", "subtask", "attach", "dependency", "comment"];

    sealed record Roles(bool Owner, bool Assigner, bool Closer, bool Supervisor, bool Creator, HashSet<string> Principals)
    {
        /// <summary>The officer acted for, when the caller is not the named officer themselves.</summary>
        public string? ActingFor(string meId, params string?[] named) => named.Contains(meId) ? null : named.FirstOrDefault(n => n is not null && Principals.Contains(n));
    }

    async Task<Roles> RolesAsync(User me, WorkTask t, CancellationToken ct)
    {
        var p = await h.PrincipalsAsync(me.Id, DelegationScope.Tasks, null, ct);
        var supervisor = false;
        if (t.OwnerId is not null) foreach (var pr in p) if ((await h.SubordinatesAsync(pr, ct)).Contains(t.OwnerId)) { supervisor = true; break; }
        return new Roles(t.OwnerId is not null && p.Contains(t.OwnerId), p.Contains(t.AssignedById), p.Contains(t.ClosureAuthorityId ?? t.AssignedById), supervisor, p.Contains(t.CreatedById), p);
    }

    static string[] Allowed(WorkTask t, Roles r, User me)
    {
        var a = new List<string> { "comment" };
        var s = t.Status;
        var final = TaskLifecycle.IsFinal(s);
        if ((s == WorkTaskStatus.Draft && (r.Assigner || r.Creator)) || (!final && s != WorkTaskStatus.Draft && r.Assigner)) a.Add("edit");
        if (s == WorkTaskStatus.Draft && (r.Assigner || r.Creator)) a.Add("assign");
        if (s is WorkTaskStatus.Assigned or WorkTaskStatus.Returned && r.Owner) a.Add("start");
        if (s is WorkTaskStatus.InProgress or WorkTaskStatus.Blocked or WorkTaskStatus.Returned && r.Owner) a.Add("progress");
        if (s is WorkTaskStatus.Assigned or WorkTaskStatus.InProgress or WorkTaskStatus.Returned && r.Owner) a.Add("block");
        if (s == WorkTaskStatus.Blocked && (r.Owner || r.Assigner || r.Supervisor)) a.Add("unblock");
        if (s is WorkTaskStatus.InProgress or WorkTaskStatus.Returned && r.Owner) a.Add("submit");
        if (s == WorkTaskStatus.SubmittedForReview && r.Closer) { a.Add("close"); a.Add("return"); }
        if (s == WorkTaskStatus.Completed && r.Closer) a.Add("reopen");
        if (!final && (r.Assigner || r.Closer || (s == WorkTaskStatus.Draft && r.Creator))) a.Add("cancel");
        if (!final && (r.Assigner || r.Supervisor)) a.Add("reassign");
        if (!final && s != WorkTaskStatus.Draft && (r.Owner || r.Assigner) && AccessPolicy.Can(me, Permissions.CreateTask)) a.Add("subtask");
        if (!final && (r.Owner || r.Assigner || r.Closer)) a.Add("attach");
        if (!final && (r.Owner || r.Assigner)) a.Add("dependency");
        return [.. a];
    }

    async Task<(WorkTask Task, Roles Roles, string[] Allowed)> LoadForAsync(User me, int id, string action, CancellationToken ct)
    {
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Task");
        if (!await CanSeeAsync(me, t, ct)) throw AppException.NotFound("Task");
        var r = await RolesAsync(me, t, ct);
        var allowed = Allowed(t, r, me);
        if (!allowed.Contains(action))
            throw await audit.DenyAsync("task", "task-" + action, me, "task", t.Id.ToString(), $"You cannot {action} task {t.Number} while it is {Label(t.Status)}.", ct, 403, "task-permission");
        return (t, r, allowed);
    }

    public async Task<bool> CanSeeAsync(User me, WorkTask t, CancellationToken ct)
    {
        if (AccessPolicy.Can(me, Permissions.Audit)) return true;
        var v = await h.VisibleOwnersAsync(me.Id, ct);
        return (t.OwnerId is not null && v.Contains(t.OwnerId)) || v.Contains(t.AssignedById) || (t.ClosureAuthorityId is not null && v.Contains(t.ClosureAuthorityId)) || v.Contains(t.CreatedById);
    }

    public async Task<IQueryable<WorkTask>> VisibleQueryAsync(User me, CancellationToken ct)
    {
        var q = db.Tasks.AsNoTracking();
        if (AccessPolicy.Can(me, Permissions.Audit)) return q;
        var v = (await h.VisibleOwnersAsync(me.Id, ct)).ToList();
        return q.Where(t => (t.OwnerId != null && v.Contains(t.OwnerId)) || v.Contains(t.AssignedById) || (t.ClosureAuthorityId != null && v.Contains(t.ClosureAuthorityId)) || v.Contains(t.CreatedById));
    }

    public static string Label(WorkTaskStatus s) => s switch { WorkTaskStatus.InProgress => "In Progress", WorkTaskStatus.SubmittedForReview => "Submitted for Review", _ => s.ToString() };

    // ---- queries ------------------------------------------------------------------------------------------
    public async Task<List<TaskDto>> ListAsync(string? scope, string? status, string? q, string? ownerId, string? priority, int? parentId, bool? topLevel, DateTime? dueFrom, DateTime? dueTo, int? unitId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var query = await VisibleQueryAsync(me, ct);
        var principals = (await h.PrincipalsAsync(me.Id, DelegationScope.Tasks, null, ct)).ToList();
        switch (scope)
        {
            case "mine": query = query.Where(t => t.OwnerId != null && principals.Contains(t.OwnerId)); break;
            case "assigned": query = query.Where(t => principals.Contains(t.AssignedById)); break;
            case "team":
                var team = new HashSet<string>(); foreach (var p in principals) team.UnionWith(await h.SubordinatesAsync(p, ct));
                var tl = team.ToList();
                query = query.Where(t => t.OwnerId != null && tl.Contains(t.OwnerId)); break;
            case "review": query = query.Where(t => t.Status == WorkTaskStatus.SubmittedForReview && principals.Contains(t.ClosureAuthorityId ?? t.AssignedById)); break;
        }
        var today = DateTime.UtcNow.Date;
        if (status == "open") query = query.Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled && t.Status != WorkTaskStatus.Draft);
        else if (status == "overdue") query = query.Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled && t.Status != WorkTaskStatus.Draft && t.DueOn < today);
        else if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkTaskStatus>(status, out var st)) query = query.Where(t => t.Status == st);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); query = query.Where(t => t.Number.Contains(s) || t.Title.Contains(s) || (t.Purpose ?? "").Contains(s)); }
        if (!string.IsNullOrEmpty(ownerId)) query = query.Where(t => t.OwnerId == ownerId);
        if (!string.IsNullOrEmpty(priority) && Enum.TryParse<TaskPriority>(priority, out var pr)) query = query.Where(t => t.Priority == pr);
        if (parentId is int pid) query = query.Where(t => t.ParentId == pid);
        if (topLevel == true) query = query.Where(t => t.ParentId == null);
        if (dueFrom is DateTime df) query = query.Where(t => t.DueOn >= df.Date);
        if (dueTo is DateTime dt) query = query.Where(t => t.DueOn <= dt.Date);
        if (unitId is int u) query = query.Where(t => t.OrgUnitId == u);
        var rows = await query.Include(t => t.Category).Include(t => t.Parent).OrderByDescending(t => t.Priority).ThenBy(t => t.DueOn == null).ThenBy(t => t.DueOn).ThenByDescending(t => t.Id).Take(1000).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<List<TaskDto>> ToDtosAsync(List<WorkTask> rows, CancellationToken ct)
    {
        var all = await h.UsersAsync(ct);
        var ids = rows.Select(r => (int?)r.Id).ToList();
        var stats = await db.Tasks.AsNoTracking().Where(c => ids.Contains(c.ParentId)).GroupBy(c => c.ParentId)
            .Select(g => new { Id = g.Key, Total = g.Count(), Done = g.Count(c => c.Status == WorkTaskStatus.Completed || c.Status == WorkTaskStatus.Cancelled) }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var parentNos = await db.Tasks.AsNoTracking().Where(t => rows.Select(r => r.ParentId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, ct);
        var cats = await db.TaskCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(t =>
        {
            var s = stats.FirstOrDefault(x => x.Id == t.Id);
            return ToDto(t, all, s?.Total ?? 0, s?.Done ?? 0, t.OrgUnitId is int u ? units.GetValueOrDefault(u) : null, t.ParentId is int p ? parentNos.GetValueOrDefault(p) : null, t.CategoryId is int c ? cats.GetValueOrDefault(c) : null);
        }).ToList();
    }

    static TaskDto ToDto(WorkTask t, Dictionary<string, User> all, int children, int done, string? unit, string? parentNo, string? category)
    {
        var now = DateTime.UtcNow;
        var overdue = t.IsOverdue(now);
        return new TaskDto(t.Id, t.Number, t.Title, t.Priority.ToString(), t.Status.ToString(), t.OwnerId, t.OwnerId is null ? null : all.GetValueOrDefault(t.OwnerId)?.Name,
            t.AssignedById, all.GetValueOrDefault(t.AssignedById)?.Name, t.ParentId, parentNo, t.DueOn, t.Progress, overdue, overdue ? (int)(now.Date - t.DueOn!.Value.Date).TotalDays : 0,
            t.BlockedSince is DateTime b && t.Status == WorkTaskStatus.Blocked ? (int)(now - b).TotalDays : null, children, done, category, unit, t.CreatedAt, t.SubmittedAt, t.ClosedAt, t.EscalationLevel);
    }

    public async Task<TaskDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var t = await db.Tasks.AsNoTracking().Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Task");
        if (!await CanSeeAsync(me, t, ct)) throw AppException.NotFound("Task");
        var all = await h.UsersAsync(ct);
        var roles = await RolesAsync(me, t, ct);
        var dto = (await ToDtosAsync([t], ct))[0];

        var ancestors = new List<TaskRefDto>();
        var pid = t.ParentId; var guard = 0;
        while (pid is int p && guard++ < 20)
        {
            var parent = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == p, ct);
            if (parent is null) break;
            ancestors.Insert(0, new TaskRefDto(parent.Id, parent.Number, parent.Title, parent.Status.ToString()));
            pid = parent.ParentId;
        }
        var children = await ToDtosAsync(await db.Tasks.AsNoTracking().Where(c => c.ParentId == id).OrderBy(c => c.Id).ToListAsync(ct), ct);
        var deps = await db.TaskDependencies.AsNoTracking().Where(d => d.TaskId == id).Include(d => d.DependsOn).Select(d => d.DependsOn!).ToListAsync(ct);
        var acts = await db.TaskActivities.AsNoTracking().Where(a => a.TaskId == id).OrderBy(a => a.Id).ToListAsync(ct);
        var fileIds = acts.Where(a => a.FileId != null).Select(a => a.FileId!.Value).ToList();
        var fileNames = await db.Files.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.FileName, ct);
        var atts = await db.TaskAttachments.AsNoTracking().Where(a => a.TaskId == id).Include(a => a.File).OrderBy(a => a.Id).ToListAsync(ct);
        var docs = await db.Documents.AsNoTracking().Where(d => d.TaskId == id).Include(d => d.Route).ToListAsync(ct);
        string N(string? uid) => uid is null ? "—" : all.GetValueOrDefault(uid)?.Name ?? uid;

        return new TaskDetailDto(dto, t.Purpose, t.Instructions, t.Deliverable, t.ProgressNote, t.NextAction, t.ForecastOn, t.Blocker, t.BlockedSince,
            t.ClosureAuthorityId ?? t.AssignedById, N(t.ClosureAuthorityId ?? t.AssignedById), t.ClosedById, t.ClosedById is null ? null : N(t.ClosedById), t.ClosureNote,
            t.ReturnCount, t.RequireDocumentsComplete, t.CategoryId, ancestors, children,
            deps.Select(d => new TaskRefDto(d.Id, d.Number, d.Title, d.Status.ToString())).ToList(),
            acts.Select(a => new TaskActivityDto(a.Id, a.At, a.ActorId, N(a.ActorId), a.ActingForId, a.ActingForId is null ? null : N(a.ActingForId), a.Event, a.Stage.ToString(),
                a.OldValue, a.NewValue, a.Comment, a.ReplyToId, a.FileId, a.FileId is Guid g ? fileNames.GetValueOrDefault(g) : null,
                (a.Mentions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))).ToList(),
            atts.Select(a => new TaskAttachmentDto(a.Id, a.FileId, a.File!.FileName, a.File.ContentType, a.File.Size, a.File.Sha256, a.Kind, a.Note, a.Stage.ToString(), N(a.File.UploadedById), a.File.UploadedAt)).ToList(),
            docs.Select(d => new DocumentRefDto(d.Id, d.Number, d.Subject, d.Status.ToString(), string.Join(", ", d.Route.Where(r => r.Status == StageStatus.Active && r.Blocking).Select(r => N(r.UserId))))).ToList(),
            Allowed(t, roles, me), deps.Count(d => !TaskLifecycle.IsFinal(d.Status)));
    }

    // ---- create / edit -------------------------------------------------------------------------------------
    async Task<HashSet<string>> AssignableIdsAsync(User me, CancellationToken ct)
    {
        var ids = new HashSet<string> { me.Id };
        foreach (var p in await h.PrincipalsAsync(me.Id, DelegationScope.Tasks, null, ct)) { ids.Add(p); ids.UnionWith(await h.SubordinatesAsync(p, ct)); }
        return ids;
    }

    public async Task<TaskDetailDto> CreateAsync(TaskUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.CreateTask, ct);
        var t = await CreateInternalAsync(me, r, ct);
        return await GetAsync(t.Id, ct);
    }

    async Task<WorkTask> CreateInternalAsync(User me, TaskUpsert r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw AppException.Bad("Title is required.");
        var all = await h.UsersAsync(ct);
        WorkTask? parent = null;
        string? actingFor = null;
        if (r.ParentId is int pid)
        {
            parent = await db.Tasks.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw AppException.NotFound("Parent task");
            if (!await CanSeeAsync(me, parent, ct)) throw AppException.NotFound("Parent task");
            var pr = await RolesAsync(me, parent, ct);
            if (!Allowed(parent, pr, me).Contains("subtask")) throw AppException.Forbidden($"You cannot add sub-tasks to {parent.Number} while it is {Label(parent.Status)}.", "task-permission");
            actingFor = pr.ActingFor(me.Id, parent.OwnerId, parent.AssignedById);
        }
        var assignable = await AssignableIdsAsync(me, ct);
        var ownerId = string.IsNullOrWhiteSpace(r.OwnerId) ? null : r.OwnerId;
        if (ownerId is not null)
        {
            if (!all.TryGetValue(ownerId, out var owner) || !owner.Active) throw AppException.Bad("The owner is not an active user.");
            if (!assignable.Contains(ownerId)) throw AppException.Forbidden($"{owner.Name} is not in your reporting line. Tasks are assigned down the hierarchy (BRD 6).", "hierarchy");
        }
        var closer = string.IsNullOrWhiteSpace(r.ClosureAuthorityId) ? null : r.ClosureAuthorityId;
        if (closer is not null && closer != me.Id)
        {
            var chain = await h.ChainAboveAsync(me.Id, ct);
            if (!chain.Contains(closer) && !assignable.Contains(closer)) throw AppException.Bad("The closure authority must be you, an officer above you, or an officer in your reporting line.");
        }
        if (r.CategoryId is int cid && !await db.TaskCategories.AnyAsync(c => c.Id == cid, ct)) throw AppException.Bad("Unknown category.");
        var t = new WorkTask
        {
            Number = await numbers.NextAsync("TSK", ct), Title = r.Title.Trim(), Purpose = r.Purpose, CategoryId = r.CategoryId, Priority = r.Priority, OwnerId = ownerId,
            ClosureAuthorityId = closer, DueOn = r.DueOn?.Date, Deliverable = r.Deliverable, Instructions = r.Instructions, ParentId = parent?.Id,
            RequireDocumentsComplete = r.RequireDocumentsComplete, AssignedById = actingFor ?? me.Id, CreatedById = me.Id,
            OrgUnitId = (ownerId is null ? me.OrgUnitId : all[ownerId].OrgUnitId) ?? me.OrgUnitId,
        };
        t.Activities.Add(new TaskActivity { ActorId = me.Id, ActingForId = actingFor, Event = "created", Stage = WorkTaskStatus.Draft, NewValue = t.Title, Comment = parent is null ? null : $"Sub-task of {parent.Number}" });
        db.Tasks.Add(t);
        if (r.Assign) AssignInternal(t, me, actingFor, all);
        await db.SaveChangesAsync(ct);
        FixNotifyIds(t);
        audit.Log("task", "task-created", me, "task", t.Id.ToString(), next: new { t.Number, t.Title, t.OwnerId, t.DueOn, Priority = t.Priority.ToString(), t.ParentId, Status = t.Status.ToString() }, actingForId: actingFor);
        if (parent is not null) db.TaskActivities.Add(new TaskActivity { TaskId = parent.Id, ActorId = me.Id, ActingForId = actingFor, Event = "subtask", Stage = parent.Status, NewValue = t.Number, Comment = $"Sub-task {t.Number} \"{t.Title}\" created" + (t.OwnerId is null ? "" : $" for {all[t.OwnerId].Name}") });
        await db.SaveChangesAsync(ct);
        return t;
    }

    void AssignInternal(WorkTask t, User me, string? actingFor, Dictionary<string, User> all)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Title)) missing.Add("title");
        if (t.OwnerId is null) missing.Add("owner");
        if (t.DueOn is null) missing.Add("due date");
        if (string.IsNullOrWhiteSpace(t.Deliverable)) missing.Add("expected deliverable");
        if (missing.Count > 0) throw AppException.Bad("Before assigning, fill in: " + string.Join(", ", missing) + ".", "required");
        if (t.DueOn!.Value.Date < DateTime.UtcNow.Date) throw AppException.Bad("The due date is in the past.");
        Transition(t, WorkTaskStatus.Assigned);
        t.AssignedAt = DateTime.UtcNow;
        t.Activities.Add(new TaskActivity { ActorId = me.Id, ActingForId = actingFor, Event = "assigned", Stage = WorkTaskStatus.Assigned, NewValue = all.GetValueOrDefault(t.OwnerId!)?.Name ?? t.OwnerId });
        notify.Notify([t.OwnerId], "assigned", $"Task assigned: {t.Number}", $"{t.Title} · due {t.DueOn:dd-MM-yyyy}", "task", t.Id == 0 ? "" : t.Id.ToString(), me.Id);
    }

    static void Transition(WorkTask t, WorkTaskStatus to)
    {
        if (!TaskLifecycle.Allowed(t.Status, to)) throw AppException.Rule("task-status", $"A task cannot go from {Label(t.Status)} to {Label(to)}.");
        t.Status = to;
    }

    public async Task<TaskDetailDto> UpdateAsync(int id, TaskUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "edit", ct);
        var actingFor = roles.ActingFor(me.Id, t.AssignedById, t.CreatedById);
        if (string.IsNullOrWhiteSpace(r.Title)) throw AppException.Bad("Title is required.");
        var prev = new { t.Title, t.DueOn, Priority = t.Priority.ToString(), t.Deliverable, t.ClosureAuthorityId, t.OwnerId };
        var changes = new List<string>();
        void Note(string field, string? o, string? n) { if ((o ?? "") != (n ?? "")) changes.Add($"{field}: {(string.IsNullOrEmpty(o) ? "—" : o)} → {(string.IsNullOrEmpty(n) ? "—" : n)}"); }
        Note("Title", t.Title, r.Title.Trim()); Note("Due", t.DueOn?.ToString("dd-MM-yyyy"), r.DueOn?.ToString("dd-MM-yyyy")); Note("Priority", t.Priority.ToString(), r.Priority.ToString());
        Note("Deliverable", t.Deliverable, r.Deliverable);
        if (t.Status != WorkTaskStatus.Draft && r.DueOn is null) throw AppException.Bad("An assigned task must keep a due date.");
        t.Title = r.Title.Trim(); t.Purpose = r.Purpose; t.CategoryId = r.CategoryId; t.Priority = r.Priority; t.DueOn = r.DueOn?.Date; t.Deliverable = r.Deliverable;
        t.Instructions = r.Instructions; t.RequireDocumentsComplete = r.RequireDocumentsComplete;
        if (!string.IsNullOrWhiteSpace(r.ClosureAuthorityId) && r.ClosureAuthorityId != (t.ClosureAuthorityId ?? t.AssignedById))
        {
            var chain = await h.ChainAboveAsync(me.Id, ct);
            if (r.ClosureAuthorityId != me.Id && !chain.Contains(r.ClosureAuthorityId)) throw AppException.Bad("The closure authority must be you or an officer above you.");
            Note("Closure authority", await h.NameAsync(t.ClosureAuthorityId ?? t.AssignedById, ct), await h.NameAsync(r.ClosureAuthorityId, ct));
            t.ClosureAuthorityId = r.ClosureAuthorityId;
        }
        if (t.Status == WorkTaskStatus.Draft && r.OwnerId != t.OwnerId)
        {
            if (r.OwnerId is not null && !(await AssignableIdsAsync(me, ct)).Contains(r.OwnerId)) throw AppException.Forbidden("That officer is not in your reporting line.", "hierarchy");
            Note("Owner", await h.NameAsync(t.OwnerId, ct), await h.NameAsync(r.OwnerId, ct));
            t.OwnerId = r.OwnerId;
        }
        if (changes.Count > 0) db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "updated", Stage = t.Status, Comment = string.Join("; ", changes) });
        if (r.Assign && t.Status == WorkTaskStatus.Draft) { AssignInternal(t, me, actingFor, await h.UsersAsync(ct)); FixNotifyIds(t); }
        audit.Log("task", "task-updated", me, "task", t.Id.ToString(), prev, new { t.Title, t.DueOn, Priority = t.Priority.ToString(), t.Deliverable, t.ClosureAuthorityId, t.OwnerId }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Notices queued before the task had an id carry an empty entity id; fill it in.</summary>
    void FixNotifyIds(WorkTask t)
    {
        foreach (var n in db.Notifications.Local.Where(n => n.EntityType == "task" && n.EntityId == "")) n.EntityId = t.Id.ToString();
    }

    // ---- actions ---------------------------------------------------------------------------------------------
    public async Task<TaskDetailDto> ActAsync(int id, string action, TaskActionRequest r, CancellationToken ct)
    {
        action = (action ?? "").ToLowerInvariant();
        if (!new[] { "assign", "start", "block", "unblock", "submit", "close", "return", "reopen", "cancel", "reassign" }.Contains(action)) throw AppException.Bad($"Unknown task action \"{action}\".");
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, action, ct);
        var all = await h.UsersAsync(ct);
        var reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim();
        var before = t.Status;
        string? actingFor = null;
        string N(string? uid) => uid is null ? "—" : all.GetValueOrDefault(uid)?.Name ?? uid;
        void Need(string what) { if (reason is null) throw AppException.Bad($"Give a reason to {what}.", "reason"); }
        var closer = t.ClosureAuthorityId ?? t.AssignedById;

        switch (action)
        {
            case "assign":
                actingFor = roles.ActingFor(me.Id, t.AssignedById, t.CreatedById);
                AssignInternal(t, me, actingFor, all);
                break;
            case "start":
                actingFor = roles.ActingFor(me.Id, t.OwnerId);
                Transition(t, WorkTaskStatus.InProgress); t.StartedAt ??= DateTime.UtcNow;
                notify.Notify([t.AssignedById], "status", $"{t.Number} accepted and started", $"{N(t.OwnerId)} started \"{t.Title}\"", "task", t.Id.ToString(), me.Id);
                break;
            case "block":
                Need("record a blocker");
                actingFor = roles.ActingFor(me.Id, t.OwnerId);
                Transition(t, WorkTaskStatus.Blocked); t.Blocker = reason; t.BlockedSince = DateTime.UtcNow;
                var supervisorsUp = (await h.ChainAboveAsync(t.OwnerId!, ct)).Take(1);
                notify.Notify([t.AssignedById, .. supervisorsUp], "blocked", $"Blocked: {t.Number}", $"{N(t.OwnerId)}: {reason}", "task", t.Id.ToString(), me.Id);
                break;
            case "unblock":
                actingFor = roles.ActingFor(me.Id, t.OwnerId, t.AssignedById);
                Transition(t, WorkTaskStatus.InProgress);
                db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "blocker-cleared", Stage = WorkTaskStatus.InProgress, OldValue = t.Blocker,
                    Comment = reason ?? $"Blocked for {(DateTime.UtcNow - (t.BlockedSince ?? DateTime.UtcNow)).TotalDays:0.#} day(s)" });
                t.Blocker = null; t.BlockedSince = null;
                notify.Notify([t.OwnerId, t.AssignedById], "status", $"{t.Number} unblocked", reason, "task", t.Id.ToString(), me.Id);
                break;
            case "submit":
                actingFor = roles.ActingFor(me.Id, t.OwnerId);
                Transition(t, WorkTaskStatus.SubmittedForReview); t.SubmittedAt = DateTime.UtcNow;
                notify.Notify([closer], "review", $"For review: {t.Number}", $"{N(t.OwnerId)} submitted \"{t.Title}\" for review" + (reason is null ? "" : $": {reason}"), "task", t.Id.ToString(), me.Id);
                break;
            case "close":
                actingFor = roles.ActingFor(me.Id, closer);
                var openChildren = await db.Tasks.Where(c => c.ParentId == t.Id && c.Status != WorkTaskStatus.Completed && c.Status != WorkTaskStatus.Cancelled).Select(c => c.Number).ToListAsync(ct);
                if (openChildren.Count > 0) throw AppException.Rule("open-subtasks", $"Close or cancel the sub-tasks first: {string.Join(", ", openChildren)}.");
                if (t.RequireDocumentsComplete)
                {
                    var openDocs = await db.Documents.Where(d => d.TaskId == t.Id && d.Status != DocumentStatus.Completed).Select(d => d.Number).ToListAsync(ct);
                    if (openDocs.Count > 0) throw AppException.Rule("open-documents", $"This task needs its documents completed first: {string.Join(", ", openDocs)}. Uploading a file does not complete a task (BRD 6).");
                }
                Transition(t, WorkTaskStatus.Completed); t.ClosedAt = DateTime.UtcNow; t.ClosedById = me.Id; t.ClosureNote = reason; t.Progress = 100;
                notify.Notify([t.OwnerId, t.AssignedById], "closed", $"Completed: {t.Number}", $"Output accepted and closed by {me.Name}" + (reason is null ? "" : $": {reason}"), "task", t.Id.ToString(), me.Id);
                if (t.ParentId is int pid) db.TaskActivities.Add(new TaskActivity { TaskId = pid, ActorId = me.Id, Event = "subtask", Stage = (await db.Tasks.FirstAsync(x => x.Id == pid, ct)).Status, NewValue = t.Number, Comment = $"Sub-task {t.Number} completed" });
                break;
            case "return":
                Need("return the task");
                actingFor = roles.ActingFor(me.Id, closer);
                Transition(t, WorkTaskStatus.Returned); t.ReturnCount += 1;
                notify.Notify([t.OwnerId], "returned", $"Returned: {t.Number}", reason, "task", t.Id.ToString(), me.Id);
                break;
            case "reopen":
                Need("reopen the task");
                actingFor = roles.ActingFor(me.Id, closer);
                Transition(t, WorkTaskStatus.InProgress); t.ClosedAt = null; t.ClosedById = null; t.ClosureNote = null;
                notify.Notify([t.OwnerId], "reopened", $"Reopened: {t.Number}", reason, "task", t.Id.ToString(), me.Id);
                break;
            case "cancel":
                Need("cancel the task");
                actingFor = roles.ActingFor(me.Id, t.AssignedById, closer, t.CreatedById);
                var liveChildren = await db.Tasks.Where(c => c.ParentId == t.Id && c.Status != WorkTaskStatus.Completed && c.Status != WorkTaskStatus.Cancelled).Select(c => c.Number).ToListAsync(ct);
                if (liveChildren.Count > 0) throw AppException.Rule("open-subtasks", $"Cancel or close the sub-tasks first: {string.Join(", ", liveChildren)}.");
                Transition(t, WorkTaskStatus.Cancelled); t.ClosedAt = DateTime.UtcNow; t.ClosedById = me.Id; t.ClosureNote = reason;
                notify.Notify([t.OwnerId], "cancelled", $"Cancelled: {t.Number}", reason, "task", t.Id.ToString(), me.Id);
                break;
            case "reassign":
                Need("reassign the task");
                if (string.IsNullOrWhiteSpace(r.OwnerId) || r.OwnerId == t.OwnerId) throw AppException.Bad("Choose the new owner.");
                if (!all.TryGetValue(r.OwnerId, out var nu) || !nu.Active) throw AppException.Bad("The new owner is not an active user.");
                if (!(await AssignableIdsAsync(me, ct)).Contains(r.OwnerId)) throw AppException.Forbidden($"{nu.Name} is not in your reporting line.", "hierarchy");
                actingFor = roles.ActingFor(me.Id, t.AssignedById);
                var old = t.OwnerId;
                db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "reassigned", Stage = t.Status, OldValue = N(old), NewValue = nu.Name, Comment = reason });
                t.OwnerId = r.OwnerId; t.OrgUnitId = nu.OrgUnitId ?? t.OrgUnitId;
                // the new owner acknowledges afresh; the original assignment stays in the history
                if (t.Status is WorkTaskStatus.InProgress or WorkTaskStatus.Blocked or WorkTaskStatus.Returned) { t.Status = WorkTaskStatus.Assigned; t.BlockedSince = null; }
                t.EscalationLevel = 0; t.RemindedAt = null;
                notify.Notify([r.OwnerId], "assigned", $"Task reassigned to you: {t.Number}", $"{t.Title} · due {t.DueOn:dd-MM-yyyy}. Reason: {reason}", "task", t.Id.ToString(), me.Id);
                notify.Notify([old, t.AssignedById], "reassigned", $"{t.Number} reassigned to {nu.Name}", reason, "task", t.Id.ToString(), me.Id);
                break;
        }
        if (action is not ("reassign" or "unblock" or "assign"))
            db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = action == "assign" ? "assigned" : "status", Stage = t.Status, OldValue = Label(before), NewValue = Label(t.Status), Comment = reason });
        FixNotifyIds(t);
        audit.Log("task", "task-" + action, me, "task", t.Id.ToString(), new { Status = before.ToString() }, new { Status = t.Status.ToString(), t.OwnerId }, reason, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> ProgressAsync(int id, TaskProgressRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "progress", ct);
        if (r.Progress is < 0 or > 100) throw AppException.Bad("Progress is a percentage from 0 to 100.");
        var actingFor = roles.ActingFor(me.Id, t.OwnerId);
        var prev = new { t.Progress, t.ProgressNote, t.NextAction, t.ForecastOn };
        db.TaskActivities.Add(new TaskActivity
        {
            TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "progress", Stage = t.Status, OldValue = $"{t.Progress}%", NewValue = $"{r.Progress}%",
            Comment = string.Join(" · ", new[] { r.ProgressNote, r.NextAction is null ? null : "Next: " + r.NextAction, r.ForecastOn is null ? null : $"Forecast {r.ForecastOn:dd-MM-yyyy}" }.Where(x => !string.IsNullOrWhiteSpace(x))),
        });
        t.Progress = r.Progress; t.ProgressNote = r.ProgressNote; t.NextAction = r.NextAction; t.ForecastOn = r.ForecastOn?.Date;
        audit.Log("task", "task-progress", me, "task", t.Id.ToString(), prev, new { t.Progress, t.ProgressNote, t.NextAction, t.ForecastOn }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    [GeneratedRegex(@"@([a-z0-9._\-]{3,48})", RegexOptions.IgnoreCase)]
    private static partial Regex MentionRx();

    public async Task<TaskDetailDto> CommentAsync(int id, CommentRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "comment", ct);
        if (string.IsNullOrWhiteSpace(r.Text)) throw AppException.Bad("Write the comment.");
        if (r.ReplyToId is long rid && !await db.TaskActivities.AnyAsync(a => a.Id == rid && a.TaskId == id, ct)) throw AppException.Bad("The comment you are replying to is not on this task.");
        var all = await h.UsersAsync(ct);
        var mentions = MentionRx().Matches(r.Text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Where(all.ContainsKey).Distinct().ToList();
        var actingFor = roles.ActingFor(me.Id, t.OwnerId, t.AssignedById, t.ClosureAuthorityId);
        db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "comment", Stage = t.Status, Comment = r.Text.Trim(), ReplyToId = r.ReplyToId, Mentions = mentions.Count > 0 ? string.Join(',', mentions) : null });
        notify.Notify([t.OwnerId, t.AssignedById, t.ClosureAuthorityId, .. mentions], "comment", $"Comment on {t.Number}", $"{me.Name}: {r.Text.Trim()}", "task", t.Id.ToString(), me.Id);
        audit.Log("task", "task-comment", me, "task", t.Id.ToString(), actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> AttachAsync(int id, string fileName, string? contentType, byte[] bytes, string? kind, string? note, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "attach", ct);
        var cfg = await configs.GetAsync(ct);
        var f = await files.StoreAsync($"tasks/{t.Id}", fileName, contentType, bytes, cfg.Files.TaskAttachmentExtensions, cfg.Files.TaskAttachmentMaxMb, me.Id, ct);
        var k = kind is "instruction" or "deliverable" ? kind : "evidence";
        db.TaskAttachments.Add(new TaskAttachment { TaskId = t.Id, FileId = f.Id, Kind = k, Note = note, Stage = t.Status });
        var actingFor = roles.ActingFor(me.Id, t.OwnerId, t.AssignedById, t.ClosureAuthorityId);
        db.TaskActivities.Add(new TaskActivity { TaskId = t.Id, ActorId = me.Id, ActingForId = actingFor, Event = "attachment", Stage = t.Status, NewValue = f.FileName, Comment = note, FileId = f.Id });
        audit.Log("task", "task-attachment", me, "task", t.Id.ToString(), next: new { f.FileName, f.Size, f.Sha256, Kind = k }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<(byte[] Bytes, string Name, string Type)> DownloadAsync(int id, int attachmentId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var t = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Task");
        if (!await CanSeeAsync(me, t, ct)) throw AppException.NotFound("Task");
        var a = await db.TaskAttachments.AsNoTracking().Include(x => x.File).FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == id, ct) ?? throw AppException.NotFound("Attachment");
        var bytes = await files.ReadAsync(a.File!, ct);
        audit.Log("task", "attachment-download", me, "task", t.Id.ToString(), next: new { a.File!.FileName, a.FileId });
        await db.SaveChangesAsync(ct);
        return (bytes, a.File.FileName, a.File.ContentType);
    }

    public async Task<TaskDetailDto> AddDependencyAsync(int id, int dependsOnId, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "dependency", ct);
        if (dependsOnId == id) throw AppException.Bad("A task cannot depend on itself.");
        var other = await db.Tasks.FirstOrDefaultAsync(x => x.Id == dependsOnId, ct) ?? throw AppException.NotFound("Task");
        if (!await CanSeeAsync(me, other, ct)) throw AppException.NotFound("Task");
        if (await db.TaskDependencies.AnyAsync(d => d.TaskId == id && d.DependsOnId == dependsOnId, ct)) return await GetAsync(id, ct);
        // refuse a dependency loop: does the other task already (transitively) depend on this one?
        var edges = await db.TaskDependencies.AsNoTracking().ToListAsync(ct);
        var stack = new Stack<int>(); stack.Push(dependsOnId); var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == id) throw AppException.Rule("dependency-loop", $"{other.Number} already depends on {t.Number}; that would be a loop.");
            if (!seen.Add(cur)) continue;
            foreach (var e in edges.Where(e => e.TaskId == cur)) stack.Push(e.DependsOnId);
        }
        db.TaskDependencies.Add(new TaskDependency { TaskId = id, DependsOnId = dependsOnId });
        var actingFor = roles.ActingFor(me.Id, t.OwnerId, t.AssignedById);
        db.TaskActivities.Add(new TaskActivity { TaskId = id, ActorId = me.Id, ActingForId = actingFor, Event = "dependency", Stage = t.Status, NewValue = other.Number, Comment = $"Depends on {other.Number} \"{other.Title}\"" });
        audit.Log("task", "task-dependency-added", me, "task", id.ToString(), next: new { other.Number }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDetailDto> RemoveDependencyAsync(int id, int dependsOnId, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var (t, roles, _) = await LoadForAsync(me, id, "dependency", ct);
        var d = await db.TaskDependencies.FirstOrDefaultAsync(x => x.TaskId == id && x.DependsOnId == dependsOnId, ct);
        if (d is not null)
        {
            db.TaskDependencies.Remove(d);
            var other = await db.Tasks.AsNoTracking().FirstAsync(x => x.Id == dependsOnId, ct);
            var actingFor = roles.ActingFor(me.Id, t.OwnerId, t.AssignedById);
            db.TaskActivities.Add(new TaskActivity { TaskId = id, ActorId = me.Id, ActingForId = actingFor, Event = "dependency", Stage = t.Status, OldValue = other.Number, Comment = $"No longer depends on {other.Number}" });
            audit.Log("task", "task-dependency-removed", me, "task", id.ToString(), prev: new { other.Number }, actingForId: actingFor);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>A manager breaks a task down: each line becomes a linked, assigned sub-task (FR-004).</summary>
    public async Task<TaskDetailDto> AddSubTasksAsync(int id, SubTasksRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.CreateTask, ct);
        if (r.Lines is null || r.Lines.Count == 0) throw AppException.Bad("Add at least one sub-task.");
        var parent = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Task");
        foreach (var l in r.Lines)
        {
            if (parent.DueOn is DateTime pd && l.DueOn.Date > pd.Date) throw AppException.Bad($"Sub-task \"{l.Title}\" is due after the parent task ({pd:dd-MM-yyyy}).");
            await CreateInternalAsync(me, new TaskUpsert(l.Title, null, parent.CategoryId, l.Priority, l.OwnerId, null, l.DueOn, l.Deliverable, l.Instructions, id, false, true), ct);
        }
        return await GetAsync(id, ct);
    }
}
