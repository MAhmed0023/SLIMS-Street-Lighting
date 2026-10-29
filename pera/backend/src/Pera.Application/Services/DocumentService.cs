using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>Document cases: registration, versions, the initiator's proposed route, the route engine (sequential steps,
/// parallel stages within a step, return, reject, information marking), controlled route amendments, the
/// controlled electronic signature and its verification (FR-009 … FR-020, SRD 5–6).</summary>
public class DocumentService(IAppDbContext db, UserContext users, HierarchyService h, AuditService audit, NotificationService notify, NumberingService numbers,
    FileService files, IPdfStamper stamper, ISigningKey signingKey, IPasswordHasher hasher)
{
    public const string SignatureMechanism = "Controlled electronic signature: password re-authentication, SHA-256 binding to the approved version, server HMAC seal and visible stamp. "
        + "It is not a certificate-based digital signature; PERA legal and IT must approve the mechanism per document category (SRD 5).";

    // ---- loading and visibility -------------------------------------------------------------------------------
    async Task<DocumentCase> LoadAsync(int id, CancellationToken ct, bool tracking = true)
    {
        var q = db.Documents.Include(d => d.DocumentType).Include(d => d.Versions).ThenInclude(v => v.File).Include(d => d.Route).Include(d => d.SignatureFields).Include(d => d.Amendments);
        var d = await (tracking ? q : q.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Document");
        return d;
    }

    async Task<(List<string> Direct, List<string> Wide)> ScopeAsync(User me, CancellationToken ct)
    {
        var direct = await h.PrincipalsAsync(me.Id, DelegationScope.All, null, ct);
        foreach (var d in await h.DelegationsAsync(ct)) if (d.ToUserId == me.Id && d.CoversDate(DateTime.UtcNow)) direct.Add(d.FromUserId);
        var wide = await h.VisibleOwnersAsync(me.Id, ct);
        return (direct.ToList(), wide.ToList());
    }

    /// <summary>Public: everyone. Internal and Confidential: participants (initiator, route officers, signers), the officers
    /// they act for, and the hierarchy above them. Restricted: participants and their acting officers only (SR-SEC-09).</summary>
    public async Task<IQueryable<DocumentCase>> VisibleQueryAsync(User me, CancellationToken ct)
    {
        var q = db.Documents.AsNoTracking();
        if (AccessPolicy.Can(me, Permissions.Audit)) return q;
        var (p, v) = await ScopeAsync(me, ct);
        return q.Where(d => d.Classification == Classification.Public
            || p.Contains(d.InitiatorId) || d.Route.Any(r => r.UserId != null && p.Contains(r.UserId))
            || (d.Classification != Classification.Restricted && (v.Contains(d.InitiatorId) || d.Route.Any(r => r.UserId != null && v.Contains(r.UserId)))));
    }

    async Task<bool> CanSeeAsync(User me, DocumentCase d, CancellationToken ct)
    {
        if (AccessPolicy.Can(me, Permissions.Audit) || d.Classification == Classification.Public) return true;
        var (p, v) = await ScopeAsync(me, ct);
        var people = d.Route.Where(r => r.UserId != null).Select(r => r.UserId!).Append(d.InitiatorId).ToList();
        return people.Any(p.Contains) || (d.Classification != Classification.Restricted && people.Any(v.Contains));
    }

    async Task<DocumentCase> LoadVisibleAsync(User me, int id, CancellationToken ct, bool tracking = true)
    {
        var d = await LoadAsync(id, ct, tracking);
        if (!await CanSeeAsync(me, d, ct)) throw AppException.NotFound("Document");
        return d;
    }

    /// <summary>Is the caller the initiator, or acting for them? Returns the officer acted for, if any.</summary>
    async Task<(bool Ok, string? ActingFor)> InitiatorAsync(User me, DocumentCase d, CancellationToken ct)
    {
        if (d.InitiatorId == me.Id) return (true, null);
        var p = await h.PrincipalsAsync(me.Id, DelegationScope.Documents, d.DocumentTypeId, ct);
        return p.Contains(d.InitiatorId) ? (true, d.InitiatorId) : (false, null);
    }

    async Task<string?> RequireInitiatorAsync(User me, DocumentCase d, string action, CancellationToken ct)
    {
        var (ok, actingFor) = await InitiatorAsync(me, d, ct);
        if (!ok) throw await audit.DenyAsync("document", action, me, "document", d.Id.ToString(), $"Only the initiator of {d.Number} (or their acting officer) can do this.", ct);
        return actingFor;
    }

    /// <summary>Who may act on a stage: its officer, or an officer acting for them with the right scope (FR-027).</summary>
    async Task<(bool Ok, string? ActingFor)> StageActorAsync(User me, DocumentCase d, RouteStage s, CancellationToken ct)
    {
        if (s.UserId == me.Id) return (true, null);
        if (s.UserId is null) return (false, null);
        var p = await h.PrincipalsAsync(me.Id, StageRules.ScopeFor(s.Type), d.DocumentTypeId, ct);
        return p.Contains(s.UserId) ? (true, s.UserId) : (false, null);
    }

    // ---- queries ------------------------------------------------------------------------------------------
    public async Task<List<DocumentDto>> ListAsync(string? box, string? status, string? q, int? typeId, string? holderId, string? classification, DateTime? from, DateTime? to, int? taskId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var query = await VisibleQueryAsync(me, ct);
        var (p, _) = await ScopeAsync(me, ct);
        switch (box)
        {
            case "inbox": query = query.Where(d => d.Status == DocumentStatus.InRoute && d.Route.Any(r => r.Status == StageStatus.Active && r.UserId != null && p.Contains(r.UserId))); break;
            case "outbox": query = query.Where(d => p.Contains(d.InitiatorId)); break;
            case "returned": query = query.Where(d => d.Status == DocumentStatus.Returned && p.Contains(d.InitiatorId)); break;
            case "signature": query = query.Where(d => d.Route.Any(r => r.Status == StageStatus.Active && r.Type == StageType.Signature)); break;
            case "approval": query = query.Where(d => d.Route.Any(r => r.Status == StageStatus.Active && (r.Type == StageType.Approval || r.Type == StageType.Concurrence))); break;
        }
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DocumentStatus>(status, out var st)) query = query.Where(d => d.Status == st);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); query = query.Where(d => d.Number.Contains(s) || d.Subject.Contains(s) || (d.ExternalRef ?? "").Contains(s)); }
        if (typeId is int t) query = query.Where(d => d.DocumentTypeId == t);
        if (!string.IsNullOrEmpty(holderId)) query = query.Where(d => d.Route.Any(r => r.Status == StageStatus.Active && r.UserId == holderId));
        if (!string.IsNullOrEmpty(classification) && Enum.TryParse<Classification>(classification, out var c)) query = query.Where(d => d.Classification == c);
        if (from is DateTime f) query = query.Where(d => d.CreatedAt >= f.Date);
        if (to is DateTime tt) { var end = tt.Date.AddDays(1); query = query.Where(d => d.CreatedAt < end); }
        if (taskId is int tid) query = query.Where(d => d.TaskId == tid);
        var rows = await query.Include(d => d.DocumentType).Include(d => d.Route).Include(d => d.Versions).OrderByDescending(d => d.Id).Take(1000).ToListAsync(ct);
        return await ToDtosAsync(rows, p, ct);
    }

    public async Task<List<DocumentDto>> ToDtosAsync(List<DocumentCase> rows, List<string> principals, CancellationToken ct)
    {
        var all = await h.UsersAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var taskIds = rows.Where(r => r.TaskId != null).Select(r => r.TaskId!.Value).ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, ct);
        return rows.Select(d => ToDto(d, all, units, tasks, principals)).ToList();
    }

    static DocumentDto ToDto(DocumentCase d, Dictionary<string, User> all, Dictionary<int, string> units, Dictionary<int, string> tasks, List<string> principals)
    {
        var now = DateTime.UtcNow;
        var active = d.Route.Where(r => r.Status == StageStatus.Active && r.Blocking).ToList();
        var holders = d.Status == DocumentStatus.Returned ? all.GetValueOrDefault(d.InitiatorId)?.Name + " (initiator)"
            : active.Count > 0 ? string.Join(", ", active.Select(r => all.GetValueOrDefault(r.UserId ?? "")?.Name ?? r.UserId)) : null;
        var av = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId);
        return new DocumentDto(d.Id, d.Number, d.Subject, d.DocumentTypeId, d.DocumentType?.Name ?? "", d.Classification.ToString(), d.Status.ToString(), d.ApprovalState.ToString(), d.SignatureState.ToString(),
            d.InitiatorId, all.GetValueOrDefault(d.InitiatorId)?.Name ?? d.InitiatorId, d.OriginOrgUnitId is int o ? units.GetValueOrDefault(o) : null, d.TaskId, d.TaskId is int t ? tasks.GetValueOrDefault(t) : null,
            d.ExternalRef, holders, active.Count > 0 ? string.Join(", ", active.Select(r => r.Name).Distinct()) : null, active.Min(r => r.ReceivedAt), active.Min(r => r.DueAt),
            active.Any(r => r.DueAt < now), d.ReturnCount, av?.No, d.CreatedAt, d.SubmittedAt, d.CompletedAt, d.Outcome,
            d.Status == DocumentStatus.InRoute && d.Route.Any(r => r.Status == StageStatus.Active && r.UserId != null && principals.Contains(r.UserId)));
    }

    public async Task<DocumentDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var d = await LoadVisibleAsync(me, id, ct, tracking: false);
        var all = await h.UsersAsync(ct);
        var (p, _) = await ScopeAsync(me, ct);
        var dto = (await ToDtosAsync([d], p, ct))[0];
        string? N(string? uid) => uid is null ? null : all.GetValueOrDefault(uid)?.Name ?? uid;
        var now = DateTime.UtcNow;
        var vno = d.Versions.ToDictionary(v => v.Id, v => v.No);
        var template = d.TemplateId is int tid ? await db.WorkflowTemplates.AsNoTracking().Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        var tStages = template?.Stages.ToDictionary(s => s.Id) ?? [];

        var versions = d.Versions.OrderByDescending(v => v.No).Select(v => new VersionDto(v.Id, v.No, v.State.ToString(), v.Id == d.ActiveVersionId, v.FileId, v.File!.FileName, v.File.ContentType, v.File.Size, v.File.Sha256,
            v.Note, N(v.CreatedById) ?? v.CreatedById, v.CreatedAt, null)).ToList();
        var route = d.Route.OrderBy(r => r.Step).ThenBy(r => r.Id).Select(r => new RouteStageDto(r.Id, r.Step, r.Name, r.Type.ToString(), r.Mandatory, r.TemplateStageId != null, r.AdHoc, r.UserId, N(r.UserId),
            r.UserId is null ? null : all.GetValueOrDefault(r.UserId)?.Designation?.Title, r.Status.ToString(), r.ReceivedAt, r.DueAt, r.ActedAt, N(r.ActedById), N(r.ActingForId), r.Outcome?.ToString(), r.Comment,
            r.VersionId is int x ? vno.GetValueOrDefault(x) : null, r.ReceivedAt is DateTime ra ? Math.Round(((r.ActedAt ?? now) - ra).TotalHours, 1) : null,
            r.Status == StageStatus.Active && r.DueAt < now, r.TemplateStageId is not int ts || !tStages.TryGetValue(ts, out var tsd) || tsd.InitiatorMayChangeAssignee)).ToList();
        var movements = await db.Movements.AsNoTracking().Where(m => m.DocumentId == id).OrderBy(m => m.Id).ToListAsync(ct);
        var fields = d.SignatureFields.OrderBy(f => f.Order).ThenBy(f => f.Id).Select(f => new SignatureFieldDto(f.Id, f.RouteStageId, d.Route.FirstOrDefault(r => r.Id == f.RouteStageId)?.Name, f.SignerUserId, N(f.SignerUserId) ?? f.SignerUserId,
            f.Order, f.Page, f.X, f.Y, f.W, f.H, f.ShowName, f.ShowDate, f.Status.ToString(), f.VersionId is int fv ? vno.GetValueOrDefault(fv) : null, f.DocumentHash, f.SignedAt, N(f.SignedById), N(f.ActingForId),
            f.AuthMethod, f.SignatureCode, f.InvalidatedReason)).ToList();

        var perms = await PermissionsAsync(me, d, ct);
        var amendments = new List<AmendmentDto>();
        foreach (var a in d.Amendments.OrderByDescending(a => a.Id))
            amendments.Add(new AmendmentDto(a.Id, a.RequestedById, N(a.RequestedById) ?? a.RequestedById, a.RequestedAt, a.Reason, a.Status, JsonSerializer.Deserialize<List<AmendmentOp>>(a.ProposalJson, ConfigService.Json) ?? [],
                N(a.DecidedById), a.DecidedAt, a.DecisionNote, a.Status == "Pending" && await CanDecideAmendmentAsync(me, a, ct)));
        var issues = d.Status is DocumentStatus.Draft or DocumentStatus.Returned ? await ValidateAsync(d, ct) : [];
        var pageCounts = new Dictionary<int, int?>();
        if (d.Status is DocumentStatus.Draft or DocumentStatus.Returned)
        {
            var av = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId);
            if (av?.File is { ContentType: "application/pdf" } file)
                try { pageCounts[av.Id] = stamper.PageCount(await files.ReadAsync(file, ct)); } catch (AppException) { /* file missing */ }
        }
        versions = versions.Select(v => pageCounts.TryGetValue(v.Id, out var pc) ? v with { Pages = pc } : v).ToList();

        return new DocumentDetailDto(dto, d.Notes, d.OriginOrgUnitId, d.TemplateId, template?.Name, d.TemplateVersion ?? template?.Version, versions, route,
            movements.Select(m => new MovementDto(m.Id, m.At, m.FromUserId, N(m.FromUserId), m.ToUserId, N(m.ToUserId), N(m.ActingForId), m.StageName, m.Action, m.ActionRequested, m.Comment, m.VersionNo, m.HeldHours)).ToList(),
            fields, amendments, issues, perms, WorkflowAdminService.ToDto(d.DocumentType!));
    }

    async Task<DocumentPermissionsDto> PermissionsAsync(User me, DocumentCase d, CancellationToken ct)
    {
        var (init, _) = await InitiatorAsync(me, d, ct);
        var stages = new List<StageActionsDto>();
        if (d.Status is DocumentStatus.InRoute or DocumentStatus.Completed)
            foreach (var s in d.Route.Where(r => r.Status == StageStatus.Active && (d.Status == DocumentStatus.InRoute || r.AdHoc)))
            {
                var (ok, actingFor) = await StageActorAsync(me, d, s, ct);
                if (ok) stages.Add(new StageActionsDto(s.Id, s.Name, s.Type.ToString(), StageRules.Actions(s.Type).Select(a => a.ToString()).ToArray(), actingFor is null ? null : await h.NameAsync(actingFor, ct)));
            }
        var approvedOrSigned = d.ApprovalState == ApprovalState.Approved || d.Route.Any(r => r.Status == StageStatus.Done && r.Type is StageType.Approval or StageType.Signature);
        var holder = d.Route.Any(r => r.Status == StageStatus.Active && r.UserId != null && stages.Any(s => s.StageId == r.Id));
        return new DocumentPermissionsDto(
            Edit: init && d.Status is DocumentStatus.Draft or DocumentStatus.Returned,
            Submit: init && d.Status is DocumentStatus.Draft or DocumentStatus.Returned,
            Withdraw: init && d.Status == DocumentStatus.InRoute && !approvedOrSigned,
            Revise: init && ((d.Status == DocumentStatus.InRoute && approvedOrSigned) || d.Status == DocumentStatus.Completed),
            Inform: (init || holder) && d.Status is DocumentStatus.InRoute or DocumentStatus.Completed,
            Amend: d.Status is DocumentStatus.InRoute or DocumentStatus.Returned && (init || holder || AccessPolicy.Can(me, Permissions.Workflow)),
            Verify: d.SignatureFields.Any(f => f.Status is SignatureFieldStatus.Signed or SignatureFieldStatus.Invalidated && f.SignatureMac != null),
            Stages: stages);
    }

    // ---- registration, metadata, versions -----------------------------------------------------------------------
    public async Task<DocumentDetailDto> CreateAsync(DocumentUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.CreateDocument, ct);
        if (string.IsNullOrWhiteSpace(r.Subject)) throw AppException.Bad("Subject is required.");
        var type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == r.DocumentTypeId && t.Active, ct) ?? throw AppException.Bad("Choose an active document type.");
        if (r.TaskId is int tid && !await db.Tasks.AnyAsync(t => t.Id == tid, ct)) throw AppException.Bad("Unknown task.");
        var d = new DocumentCase
        {
            Number = await numbers.NextAsync("DOC", ct), Subject = r.Subject.Trim(), DocumentTypeId = type.Id, OriginOrgUnitId = r.OriginOrgUnitId ?? me.OrgUnitId, Classification = r.Classification,
            TaskId = r.TaskId, ExternalRef = r.ExternalRef, Notes = r.Notes, InitiatorId = me.Id, DocumentType = type,
        };
        db.Documents.Add(d);
        var template = await PickTemplateAsync(type, r.TemplateId, ct);
        if (template is not null) await LoadTemplateRouteAsync(d, template, ct);
        await db.SaveChangesAsync(ct);
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, Action = "Registered", Comment = d.Subject });
        audit.Log("document", "document-registered", me, "document", d.Id.ToString(), next: new { d.Number, d.Subject, Type = type.Code, Classification = d.Classification.ToString(), d.TaskId, Template = template?.Code });
        if (d.TaskId is int t2)
        {
            var task = await db.Tasks.FirstAsync(x => x.Id == t2, ct);
            db.TaskActivities.Add(new TaskActivity { TaskId = t2, ActorId = me.Id, Event = "document", Stage = task.Status, NewValue = d.Number, Comment = $"Document {d.Number} \"{d.Subject}\" registered" });
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(d.Id, ct);
    }

    async Task<WorkflowTemplate?> PickTemplateAsync(DocumentType type, int? templateId, CancellationToken ct)
    {
        var now = DateTime.UtcNow.Date;
        if (templateId is int id)
        {
            var t = await db.WorkflowTemplates.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.Bad("Unknown workflow template.");
            if (t.DocumentTypeId != type.Id || t.Status != "Active" || t.EffectiveFrom > now || (t.EffectiveTo is DateTime e && e < now)) throw AppException.Bad("That template is not in force for this document type.");
            return t;
        }
        var candidates = await db.WorkflowTemplates.Include(x => x.Stages).Where(x => x.DocumentTypeId == type.Id && x.Status == "Active").ToListAsync(ct);
        candidates = candidates.Where(t => t.EffectiveFrom <= now && (t.EffectiveTo is null || t.EffectiveTo >= now)).ToList();
        // the type's default template, or the version of it now in force, or else the newest template for the type
        var defaultCode = type.DefaultTemplateId is int dt ? (await db.WorkflowTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dt, ct))?.Code : null;
        return candidates.FirstOrDefault(t => t.Id == type.DefaultTemplateId)
            ?? candidates.Where(t => t.Code == defaultCode).OrderByDescending(t => t.Version).FirstOrDefault()
            ?? candidates.OrderByDescending(t => t.Version).FirstOrDefault();
    }

    async Task LoadTemplateRouteAsync(DocumentCase d, WorkflowTemplate t, CancellationToken ct)
    {
        var all = await h.UsersAsync(ct);
        foreach (var s in t.Stages.OrderBy(s => s.Step).ThenBy(s => s.Id))
        {
            var user = s.AssigneeMode switch
            {
                AssigneeMode.User => s.UserId,
                AssigneeMode.Designation when s.DesignationId is int dg => await h.ResolveDesignationAsync(dg, d.InitiatorId, ct),
                AssigneeMode.ReportingOfficer => all.GetValueOrDefault(d.InitiatorId)?.ReportsToId,
                _ => null,
            };
            d.Route.Add(new RouteStage { Step = s.Step, Name = s.Name, Type = s.Type, Mandatory = s.Mandatory, TemplateStageId = s.Id, UserId = user, ServiceHours = s.ServiceHours });
        }
        d.TemplateId = t.Id; d.TemplateVersion = t.Version;
    }

    public async Task<DocumentDetailDto> UpdateAsync(int id, DocumentUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "document-update", ct);
        if (d.Status is not (DocumentStatus.Draft or DocumentStatus.Returned)) throw AppException.Rule("document-locked", "Details can be changed only while the document is a draft or returned to you.");
        if (string.IsNullOrWhiteSpace(r.Subject)) throw AppException.Bad("Subject is required.");
        if (r.DocumentTypeId != d.DocumentTypeId && d.Status != DocumentStatus.Draft) throw AppException.Rule("document-type-locked", "The document type cannot change after submission.");
        var prev = new { d.Subject, d.DocumentTypeId, Classification = d.Classification.ToString(), d.TaskId, d.ExternalRef };
        if (r.TaskId is int tid && !await db.Tasks.AnyAsync(t => t.Id == tid, ct)) throw AppException.Bad("Unknown task.");
        d.Subject = r.Subject.Trim(); d.OriginOrgUnitId = r.OriginOrgUnitId; d.Classification = r.Classification; d.TaskId = r.TaskId; d.ExternalRef = r.ExternalRef; d.Notes = r.Notes;
        if (r.DocumentTypeId != d.DocumentTypeId)
        {
            var type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == r.DocumentTypeId && t.Active, ct) ?? throw AppException.Bad("Choose an active document type.");
            d.DocumentTypeId = type.Id; d.DocumentType = type;
            db.RouteStages.RemoveRange(d.Route); db.SignatureFields.RemoveRange(d.SignatureFields);
            d.Route.Clear(); d.SignatureFields.Clear(); d.TemplateId = null; d.TemplateVersion = null;
            var t = await PickTemplateAsync(type, null, ct);
            if (t is not null) await LoadTemplateRouteAsync(d, t, ct);
        }
        audit.Log("document", "document-updated", me, "document", d.Id.ToString(), prev, new { d.Subject, d.DocumentTypeId, Classification = d.Classification.ToString(), d.TaskId, d.ExternalRef }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DocumentDetailDto> UploadVersionAsync(int id, string fileName, string? contentType, byte[] bytes, string? note, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "version-upload", ct);
        if (d.Status is not (DocumentStatus.Draft or DocumentStatus.Returned))
            throw AppException.Rule("document-locked", "A new version can be uploaded only while the document is a draft or returned to you. To change an approved document, use Revise — that invalidates approvals and signatures.");
        await AddVersionAsync(d, me, fileName, contentType, bytes, note, ct);
        audit.Log("document", "version-uploaded", me, "document", d.Id.ToString(), next: new { Version = d.Versions.Max(v => v.No), File = fileName }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    async Task<DocumentVersion> AddVersionAsync(DocumentCase d, User me, string fileName, string? contentType, byte[] bytes, string? note, CancellationToken ct)
    {
        var type = d.DocumentType!;
        var f = await files.StoreAsync($"documents/{d.Id}", fileName, contentType, bytes, type.AllowedExtensions, type.MaxSizeMb, me.Id, ct);
        foreach (var old in d.Versions.Where(v => v.State is VersionState.Draft or VersionState.Submitted or VersionState.Approved)) old.State = VersionState.Superseded;
        var v = new DocumentVersion { No = (d.Versions.Count == 0 ? 0 : d.Versions.Max(x => x.No)) + 1, FileId = f.Id, File = f, State = VersionState.Draft, Note = note, CreatedById = me.Id };
        d.Versions.Add(v);
        await db.SaveChangesAsync(ct);
        d.ActiveVersionId = v.Id;
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, Action = "New version", Comment = $"v{v.No}: {f.FileName}" + (note is null ? "" : $" — {note}"), VersionNo = v.No });
        return v;
    }

    public async Task<(byte[] Bytes, string Name, string Type)> DownloadVersionAsync(int id, int versionId, bool inline, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var d = await LoadVisibleAsync(me, id, ct, tracking: false);
        var v = d.Versions.FirstOrDefault(x => x.Id == versionId) ?? throw AppException.NotFound("Version");
        var bytes = await files.ReadAsync(v.File!, ct);
        // FR-010: access and download events are logged
        audit.Log("document", inline ? "version-previewed" : "version-downloaded", me, "document", d.Id.ToString(), next: new { Version = v.No, v.File!.FileName, v.File.Sha256 });
        await db.SaveChangesAsync(ct);
        return (bytes, v.File.FileName, v.File.ContentType);
    }

    public async Task<(byte[] Bytes, string Name, string Type)> DownloadSignedAsync(int id, int fieldId, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var d = await LoadVisibleAsync(me, id, ct, tracking: false);
        var f = d.SignatureFields.FirstOrDefault(x => x.Id == fieldId && x.SignedFileId != null) ?? throw AppException.NotFound("Signed file");
        var file = await db.Files.AsNoTracking().FirstAsync(x => x.Id == f.SignedFileId, ct);
        audit.Log("signature", "signed-copy-downloaded", me, "document", d.Id.ToString(), next: new { f.Id, file.FileName });
        await db.SaveChangesAsync(ct);
        return (await files.ReadAsync(file, ct), file.FileName, file.ContentType);
    }

    // ---- route builder -------------------------------------------------------------------------------------
    public async Task<DocumentDetailDto> ApplyTemplateAsync(int id, int templateId, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "route-template", ct);
        if (d.Status != DocumentStatus.Draft) throw AppException.Rule("route-locked", "After submission the route changes only through an amendment (FR-014).");
        var t = await PickTemplateAsync(d.DocumentType!, templateId, ct) ?? throw AppException.Bad("Unknown template.");
        db.SignatureFields.RemoveRange(d.SignatureFields.Where(f => f.RouteStageId != null));
        db.RouteStages.RemoveRange(d.Route.Where(r => !r.AdHoc));
        d.Route.RemoveAll(r => !r.AdHoc);
        await LoadTemplateRouteAsync(d, t, ct);
        audit.Log("document", "route-template-applied", me, "document", d.Id.ToString(), next: new { t.Code, t.Version }, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The initiator's proposed route (FR-013). Mandatory template stages cannot be dropped or retyped;
    /// assignees change only where the template allows; additions only where the template allows customisation.</summary>
    public async Task<DocumentDetailDto> SetRouteAsync(int id, RouteRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "route-proposed", ct);
        if (d.Status != DocumentStatus.Draft) throw AppException.Rule("route-locked", "After submission the route changes only through an amendment (FR-014).");
        var template = d.TemplateId is int tid ? await db.WorkflowTemplates.AsNoTracking().Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        var tStages = template?.Stages.ToDictionary(s => s.Id) ?? [];
        var before = Snapshot(d);
        var keep = new HashSet<int>();
        foreach (var l in r.Lines ?? [])
        {
            if (string.IsNullOrWhiteSpace(l.Name)) throw AppException.Bad("Every stage needs a name.");
            var user = string.IsNullOrWhiteSpace(l.UserId) ? null : l.UserId;
            if (l.Id is int sid)
            {
                var s = d.Route.FirstOrDefault(x => x.Id == sid && !x.AdHoc) ?? throw AppException.Bad($"Stage {sid} is not on this route.");
                keep.Add(sid);
                if (s.TemplateStageId is int ts && tStages.TryGetValue(ts, out var tsd))
                {
                    if (s.Mandatory && l.Type != s.Type) throw AppException.Rule("MANDATORY_STAGE", $"\"{s.Name}\" is a mandatory {s.Type.ToString().ToLower()} stage; its type cannot change.");
                    if (!tsd.InitiatorMayChangeAssignee && user != s.UserId) throw AppException.Rule("ASSIGNEE_FIXED", $"The officer for \"{s.Name}\" is fixed by the template.");
                    if (template is { AllowCustomization: false } && l.Step != s.Step) throw AppException.Rule("CUSTOMIZATION_NOT_ALLOWED", "This template does not allow stages to be reordered.");
                }
                else s.Name = l.Name.Trim();
                s.Step = l.Step; s.Type = s.Mandatory ? s.Type : l.Type; s.UserId = user;
            }
            else d.Route.Add(new RouteStage { Step = l.Step, Name = l.Name.Trim(), Type = l.Type, UserId = user });
        }
        foreach (var gone in d.Route.Where(x => !x.AdHoc && x.Id != 0 && !keep.Contains(x.Id)).ToList())
        {
            if (gone.Mandatory) throw AppException.Rule("MANDATORY_MISSING", $"Mandatory stage \"{gone.Name}\" cannot be removed from this route.");
            db.SignatureFields.RemoveRange(d.SignatureFields.Where(f => f.RouteStageId == gone.Id));
            db.RouteStages.Remove(gone); d.Route.Remove(gone);
        }
        await db.SaveChangesAsync(ct);
        // signers follow their stage
        foreach (var f in d.SignatureFields) if (d.Route.FirstOrDefault(x => x.Id == f.RouteStageId) is { } st && st.UserId is not null) f.SignerUserId = st.UserId;
        audit.Log("document", "route-proposed", me, "document", d.Id.ToString(), before, Snapshot(d), actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    static List<object> Snapshot(DocumentCase d) => d.Route.Where(r => !r.AdHoc).OrderBy(r => r.Step).ThenBy(r => r.Id)
        .Select(r => (object)new { r.Id, r.Step, r.Name, Type = r.Type.ToString(), r.UserId, r.Mandatory, Status = r.Status.ToString() }).ToList();

    public async Task<List<RouteIssue>> ValidateAsync(DocumentCase d, CancellationToken ct, IReadOnlyList<RouteLine>? lines = null)
    {
        var all = await h.UsersAsync(ct);
        var type = d.DocumentType ?? await db.DocumentTypes.AsNoTracking().FirstAsync(t => t.Id == d.DocumentTypeId, ct);
        var template = d.TemplateId is int tid ? await db.WorkflowTemplates.AsNoTracking().Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        var now = DateTime.UtcNow;
        var rules = (await db.AuthorityRules.AsNoTracking().Where(r => r.DocumentTypeId == null || r.DocumentTypeId == type.Id).ToListAsync(ct)).Where(r => r.InForce(now)).ToList();
        var ctx = new RouteContext
        {
            InitiatorId = d.InitiatorId,
            ActiveUserIds = all.Values.Where(u => u.Active).Select(u => u.Id).ToHashSet(),
            HasAuthority = (uid, kind) => all.GetValueOrDefault(uid)?.DesignationId is int dg && rules.Any(r => r.DesignationId == dg && r.Authority == kind),
            NameOf = uid => all.GetValueOrDefault(uid)?.Name ?? uid,
            MandatoryStages = template?.Stages.Where(s => s.Mandatory).Select(s => new MandatoryStage(s.Id, s.Name, s.Type)).ToList() ?? [],
            AllowParallel = type.AllowParallel, AllowCustomization = template?.AllowCustomization ?? true, RequireApprovalStage = type.RequireApprovalStage,
            AllowSelfApproval = type.AllowSelfApproval, EnforceAuthority = type.EnforceAuthority,
        };
        lines ??= d.Route.Where(r => r.Status != StageStatus.Cancelled).OrderBy(r => r.Step).ThenBy(r => r.Id).Select(r => new RouteLine(r.Step, r.Name, r.Type, r.UserId, r.Mandatory, r.TemplateStageId, r.AdHoc)).ToList();
        var issues = RouteValidator.Validate(lines, ctx);
        if (type.RequireAttachment && d.ActiveVersionId is null && d.Status is DocumentStatus.Draft or DocumentStatus.Returned)
            issues.Insert(0, new RouteIssue("error", "NO_ATTACHMENT", "Upload the working file before submitting."));
        return issues;
    }

    public async Task<List<RouteIssue>> ValidateAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct, tracking: false);
        return await ValidateAsync(d, ct);
    }

    public async Task<DocumentDetailDto> SetSignatureFieldsAsync(int id, SignatureFieldsRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "signature-fields", ct);
        if (d.Status is not (DocumentStatus.Draft or DocumentStatus.Returned)) throw AppException.Rule("document-locked", "Signature fields can be placed only before submission or while the document is returned to you.");
        var keep = new HashSet<int>();
        var order = 0;
        foreach (var f in r.Fields ?? [])
        {
            var stage = d.Route.FirstOrDefault(s => s.Id == f.RouteStageId && s.Type == StageType.Signature) ?? throw AppException.Bad("Each signature field must belong to a signature stage of this route.");
            if (stage.UserId is null) throw AppException.Bad($"Choose the officer for \"{stage.Name}\" first.");
            if (f.Page < 1) throw AppException.Bad("Pages are numbered from 1.");
            if (f.W <= 0.02 || f.H <= 0.01 || f.X < 0 || f.Y < 0 || f.X + f.W > 1.0001 || f.Y + f.H > 1.0001) throw AppException.Bad("The signature box must sit inside the page.");
            var field = f.Id is int fid ? d.SignatureFields.FirstOrDefault(x => x.Id == fid && x.Status is SignatureFieldStatus.Draft or SignatureFieldStatus.Pending) ?? throw AppException.Bad("Unknown signature field.") : new SignatureField { DocumentId = d.Id };
            field.RouteStageId = stage.Id; field.SignerUserId = stage.UserId; field.Order = ++order; field.Page = f.Page;
            field.X = Math.Round(f.X, 4); field.Y = Math.Round(f.Y, 4); field.W = Math.Round(f.W, 4); field.H = Math.Round(f.H, 4); field.ShowName = f.ShowName; field.ShowDate = f.ShowDate;
            if (field.Id == 0) d.SignatureFields.Add(field); else keep.Add(field.Id);
        }
        foreach (var gone in d.SignatureFields.Where(x => x.Id != 0 && !keep.Contains(x.Id) && x.Status is SignatureFieldStatus.Draft or SignatureFieldStatus.Pending).ToList()) { db.SignatureFields.Remove(gone); d.SignatureFields.Remove(gone); }
        audit.Log("signature", "signature-fields-placed", me, "document", d.Id.ToString(),
            next: d.SignatureFields.Where(x => x.Status is SignatureFieldStatus.Draft or SignatureFieldStatus.Pending).Select(x => new { x.SignerUserId, x.Page, x.X, x.Y, x.W, x.H }).ToList(), actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---- the route engine ------------------------------------------------------------------------------------
    public async Task<DocumentDetailDto> SubmitAsync(int id, SubmitRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "document-submit", ct);
        if (d.Status is not (DocumentStatus.Draft or DocumentStatus.Returned)) throw AppException.Rule("document-status", $"{d.Number} is {d.Status}; only a draft or returned document can be submitted.");
        var issues = await ValidateAsync(d, ct);
        var errors = issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0) throw AppException.Rule("route-invalid", "The route cannot be submitted: " + string.Join(" ", errors.Select(e => e.Message)), issues);
        if (issues.Count > 0 && !r.AcceptWarnings) throw AppException.Rule("route-warnings", "Please confirm: " + string.Join(" ", issues.Select(e => e.Message)), issues);
        var now = DateTime.UtcNow;
        var av = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId);

        // every signature stage needs a field; place a default one (bottom right of the last page) where none was drawn
        int? lastPage = null;
        foreach (var s in d.Route.Where(s => s.Type == StageType.Signature && !s.AdHoc))
            if (!d.SignatureFields.Any(f => f.RouteStageId == s.Id && f.Status is SignatureFieldStatus.Draft or SignatureFieldStatus.Pending))
            {
                if (lastPage is null && av?.File is { ContentType: "application/pdf" } pdf) lastPage = stamper.PageCount(await files.ReadAsync(pdf, ct)) ?? 1;
                d.SignatureFields.Add(new SignatureField { DocumentId = d.Id, RouteStageId = s.Id, SignerUserId = s.UserId!, Order = d.SignatureFields.Count + 1, Page = lastPage ?? 1 });
            }
        foreach (var f in d.SignatureFields.Where(f => f.Status == SignatureFieldStatus.Draft)) f.Status = SignatureFieldStatus.Pending;

        // a (re)submission starts the route afresh on the current version; earlier actions stay in the movement history
        foreach (var s in d.Route)
        {
            if (s.AdHoc) { if (s.Status == StageStatus.Active) s.Status = StageStatus.Cancelled; continue; }
            s.Status = StageStatus.Pending; s.ReceivedAt = null; s.DueAt = null; s.ActedAt = null; s.ActedById = null; s.ActingForId = null; s.Outcome = null; s.Comment = null; s.VersionId = null; s.EscalationLevel = 0; s.RemindedAt = null;
        }
        if (av is not null) av.State = VersionState.Submitted;
        d.Status = DocumentStatus.InRoute; d.SubmittedAt = now; d.CompletedAt = null; d.Outcome = null;
        d.ApprovalState = d.Route.Any(s => s.Type == StageType.Approval) ? ApprovalState.Pending : ApprovalState.NotRequired;
        d.SignatureState = d.Route.Any(s => s.Type == StageType.Signature) ? SignatureState.Pending : SignatureState.NotRequired;
        d.RouteSnapshotJson = JsonSerializer.Serialize(new { At = now, Template = d.TemplateId, d.TemplateVersion, Version = av?.No, Stages = Snapshot(d), Warnings = issues.Select(i => i.Message) }, ConfigService.Json);
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ActingForId = actingFor, Action = d.ReturnCount > 0 ? "Resubmitted" : "Submitted", VersionNo = av?.No, Comment = issues.Count > 0 ? "Submitted with warnings: " + string.Join(" ", issues.Select(i => i.Message)) : null });
        audit.Log("document", "route-submitted", me, "document", d.Id.ToString(), next: d.RouteSnapshotJson, actingForId: actingFor);
        await ActivateNextAsync(d, me.Id, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Opens the lowest pending step: all its stages go active together (parallel branches). Information stages
    /// never hold the route. When nothing blocking remains the case completes.</summary>
    async Task ActivateNextAsync(DocumentCase d, string fromUserId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var all = await h.UsersAsync(ct);
        var guard = 0;
        while (guard++ < 100)
        {
            if (d.Route.Any(s => s.Status == StageStatus.Active && s.Blocking)) return;
            var pending = d.Route.Where(s => s.Status == StageStatus.Pending && !s.AdHoc).ToList();
            if (!pending.Any(s => s.Blocking))
            {
                // information stages left behind still receive the file, then the case completes
                foreach (var s in pending) await ActivateAsync(d, s, fromUserId, now, all, ct);
                await CompleteAsync(d, ct);
                return;
            }
            var step = pending.Min(s => s.Step);
            foreach (var s in pending.Where(s => s.Step == step)) await ActivateAsync(d, s, fromUserId, now, all, ct);
        }
    }

    async Task ActivateAsync(DocumentCase d, RouteStage s, string fromUserId, DateTime now, Dictionary<string, User> all, CancellationToken ct)
    {
        s.Status = StageStatus.Active; s.ReceivedAt = now; s.VersionId = d.ActiveVersionId;
        s.DueAt = now.AddHours(s.ServiceHours ?? d.DocumentType!.StageServiceHours);
        var vno = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId)?.No;
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = fromUserId, ToUserId = s.UserId, RouteStageId = s.Id == 0 ? null : s.Id, StageName = s.Name, Action = "Forwarded", ActionRequested = StageRules.Requested(s.Type), VersionNo = vno });
        var recipients = new List<string?> { s.UserId };
        // the officer is away: anyone acting for them is told too (FRD 6: absent officers)
        var u = s.UserId is null ? null : all.GetValueOrDefault(s.UserId);
        recipients.AddRange((await h.DelegationsAsync(ct)).Where(x => x.FromUserId == s.UserId && x.CoversDate(now) && x.Covers(StageRules.ScopeFor(s.Type))).Select(x => x.ToUserId));
        notify.Notify(recipients, "route", $"{StageRules.Requested(s.Type)}: {d.Number}", $"{d.Subject} · due {s.DueAt:dd-MM-yyyy HH:mm} UTC", "document", d.Id.ToString());
        if (u is null || !u.Active)
            notify.Notify(all.Values.Where(x => x.Active && x.Role?.Has(Permissions.Workflow) == true).Select(x => (string?)x.Id).Append(d.InitiatorId), "route",
                $"Stage needs reassignment: {d.Number}", $"\"{s.Name}\" is with {u?.Name ?? "no officer"}, who is not active. Amend the route.", "document", d.Id.ToString());
    }

    async Task CompleteAsync(DocumentCase d, CancellationToken ct)
    {
        d.Status = DocumentStatus.Completed; d.CompletedAt = DateTime.UtcNow;
        var dispatch = d.Route.Where(s => s.Type == StageType.Dispatch && s.Status == StageStatus.Done).OrderByDescending(s => s.ActedAt).FirstOrDefault();
        d.Outcome = dispatch?.Outcome == StageAction.Dispatch ? "Dispatched" : "On record";
        db.Movements.Add(new Movement { DocumentId = d.Id, Action = "Completed", Comment = d.Outcome, VersionNo = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId)?.No });
        notify.Notify([d.InitiatorId], "decision", $"Completed: {d.Number}", $"{d.Subject} — {d.Outcome}", "document", d.Id.ToString());
        if (d.TaskId is int tid)
        {
            var task = await db.Tasks.FirstAsync(t => t.Id == tid, ct);
            db.TaskActivities.Add(new TaskActivity { TaskId = tid, ActorId = d.InitiatorId, Event = "document", Stage = task.Status, NewValue = d.Number, Comment = $"Document {d.Number} completed ({d.Outcome})" });
            notify.Notify([task.OwnerId, task.AssignedById], "decision", $"Linked document completed: {d.Number}", d.Subject, "task", tid.ToString());
        }
    }

    public async Task<DocumentDetailDto> ActAsync(int id, int stageId, StageActionRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var s = d.Route.FirstOrDefault(x => x.Id == stageId) ?? throw AppException.NotFound("Stage");
        if (r.Action == StageAction.Sign) throw AppException.Bad("Use the signing screen to sign; it re-confirms your identity.");
        await CheckStageAsync(me, d, s, r.Action, r.VersionId, ct);
        var (_, actingFor) = await StageActorAsync(me, d, s, ct);
        var comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();
        if (StageRules.NeedsReason(r.Action) && comment is null) throw AppException.Bad(r.Action == StageAction.Comment ? "Write the comment." : $"Give the reason to {Words(r.Action)}.", "reason");
        if (r.Action == StageAction.Approve && d.DocumentType!.ApprovalCommentRequired && comment is null) throw AppException.Bad("This document type requires a remark with every approval.", "reason");
        await ApplyActionAsync(d, s, me, actingFor, r.Action, comment, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    static string Words(StageAction a) => a switch { StageAction.NotConcur => "record non-concurrence", StageAction.Return => "return for revision", _ => a.ToString().ToLower() };

    /// <summary>Server-side checks before any stage action (SR-SEC-02): the stage is live, the caller is its officer or acting
    /// for them, the version is the one the stage received, and approval/signature power exists in the authority matrix.
    /// Refusals are written to the audit trail.</summary>
    async Task CheckStageAsync(User me, DocumentCase d, RouteStage s, StageAction action, int versionId, CancellationToken ct)
    {
        // an information copy can be acknowledged after the case has completed
        if (d.Status != DocumentStatus.InRoute && !(s.AdHoc && d.Status == DocumentStatus.Completed)) throw AppException.Rule("document-status", $"{d.Number} is {d.Status}; it is not moving on its route.");
        if (s.Status != StageStatus.Active) throw AppException.Rule("stage-status", $"\"{s.Name}\" is {s.Status}, not awaiting action.");
        if (!StageRules.Actions(s.Type).Contains(action)) throw AppException.Bad($"{action} is not an action at a {s.Type.ToString().ToLower()} stage.");
        var (ok, actingFor) = await StageActorAsync(me, d, s, ct);
        if (!ok) throw await audit.DenyAsync("document", "stage-" + action.ToString().ToLower(), me, "document", d.Id.ToString(),
            $"\"{s.Name}\" is assigned to {await h.NameAsync(s.UserId, ct)}. You hold no delegation to act for them.", ct);
        var active = d.ActiveVersionId ?? 0;
        if (versionId != active || (s.VersionId ?? 0) != active)
            throw await audit.DenyAsync("document", "stage-" + action.ToString().ToLower(), me, "document", d.Id.ToString(),
                "You are acting on a superseded version. Reload the document and review the current version before acting.", ct, 409, "superseded-version", actingFor);
        var type = d.DocumentType!;
        if (action is StageAction.Approve or StageAction.Sign)
        {
            var authorityOf = s.UserId!;
            if ((authorityOf == d.InitiatorId || me.Id == d.InitiatorId) && !type.AllowSelfApproval)
                throw await audit.DenyAsync("document", "stage-" + action.ToString().ToLower(), me, "document", d.Id.ToString(), "The initiator cannot approve or sign their own submission.", ct, 403, "self-approval", actingFor);
            var kind = action == StageAction.Approve ? AuthorityKind.Approve : AuthorityKind.Sign;
            if (type.EnforceAuthority && !await h.HasAuthorityAsync(authorityOf, type.Id, kind, ct))
                throw await audit.DenyAsync("document", "stage-" + action.ToString().ToLower(), me, "document", d.Id.ToString(),
                    $"{await h.NameAsync(authorityOf, ct)} holds no {kind.ToString().ToLower()} authority for {type.Name} under the current delegation of powers.", ct, 403, "authority", actingFor);
        }
    }

    async Task ApplyActionAsync(DocumentCase d, RouteStage s, User me, string? actingFor, StageAction action, string? comment, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var vno = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId)?.No;
        var held = s.ReceivedAt is DateTime ra ? Math.Round((now - ra).TotalHours, 2) : (double?)null;
        var kind = s.Type == StageType.Signature ? "signature" : "document";
        if (action == StageAction.Comment)
        {
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ActingForId = actingFor, RouteStageId = s.Id, StageName = s.Name, Action = "Comment", Comment = comment, VersionNo = vno });
            notify.Notify([d.InitiatorId, .. d.Route.Where(x => x.Status == StageStatus.Active).Select(x => x.UserId)], "comment", $"Comment on {d.Number}", $"{me.Name} ({s.Name}): {comment}", "document", d.Id.ToString(), me.Id);
            audit.Log(kind, "stage-comment", me, "document", d.Id.ToString(), next: new { Stage = s.Name, Version = vno }, reason: comment, actingForId: actingFor);
            return;
        }
        s.ActedAt = now; s.ActedById = me.Id; s.ActingForId = actingFor; s.Outcome = action; s.Comment = comment;
        if (StageRules.Completes(action))
        {
            s.Status = StageStatus.Done;
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ActingForId = actingFor, RouteStageId = s.Id, StageName = s.Name, Action = Past(action), Comment = comment, VersionNo = vno, HeldHours = held });
            audit.Log(kind, "stage-" + action.ToString().ToLower(), me, "document", d.Id.ToString(), next: new { Stage = s.Name, Version = vno, Outcome = action.ToString() }, reason: comment, actingForId: actingFor);
            if (action is not (StageAction.Acknowledge))
                notify.Notify([d.InitiatorId], "decision", $"{Past(action)}: {d.Number}", $"{s.Name} — {me.Name}" + (actingFor is null ? "" : $" for {await h.NameAsync(actingFor, ct)}") + (comment is null ? "" : $": {comment}"), "document", d.Id.ToString(), me.Id);
            if (action == StageAction.Approve && !d.Route.Any(x => x.Type == StageType.Approval && x.Status is StageStatus.Pending or StageStatus.Active))
            {
                d.ApprovalState = ApprovalState.Approved;
                if (d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId) is { } av) av.State = VersionState.Approved;
            }
            if (s.Blocking) await ActivateNextAsync(d, me.Id, ct);
            return;
        }
        if (action == StageAction.Return)
        {
            s.Status = StageStatus.Returned;
            foreach (var other in d.Route.Where(x => x.Id != s.Id && x.Status == StageStatus.Active && x.Blocking)) other.Status = StageStatus.Pending;
            d.Status = DocumentStatus.Returned; d.ReturnCount += 1;
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ToUserId = d.InitiatorId, ActingForId = actingFor, RouteStageId = s.Id, StageName = s.Name, Action = "Returned for revision", ActionRequested = "Revise and resubmit", Comment = comment, VersionNo = vno, HeldHours = held });
            notify.Notify([d.InitiatorId], "returned", $"Returned for revision: {d.Number}", $"{s.Name} — {me.Name}: {comment}", "document", d.Id.ToString(), me.Id);
            audit.Log(kind, "stage-return", me, "document", d.Id.ToString(), next: new { Stage = s.Name, Version = vno }, reason: comment, actingForId: actingFor);
            return;
        }
        if (action == StageAction.Reject)
        {
            s.Status = StageStatus.Rejected;
            foreach (var other in d.Route.Where(x => x.Id != s.Id && x.Status is StageStatus.Active or StageStatus.Pending)) other.Status = StageStatus.Cancelled;
            d.Status = DocumentStatus.Rejected; d.Outcome = "Rejected"; d.CompletedAt = now;
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ToUserId = d.InitiatorId, ActingForId = actingFor, RouteStageId = s.Id, StageName = s.Name, Action = "Rejected", Comment = comment, VersionNo = vno, HeldHours = held });
            notify.Notify([d.InitiatorId], "decision", $"Rejected: {d.Number}", $"{s.Name} — {me.Name}: {comment}", "document", d.Id.ToString(), me.Id);
            audit.Log(kind, "stage-reject", me, "document", d.Id.ToString(), next: new { Stage = s.Name, Version = vno }, reason: comment, actingForId: actingFor);
        }
    }

    static string Past(StageAction a) => a switch
    {
        StageAction.Acknowledge => "Acknowledged", StageAction.Recommend => "Reviewed / recommended", StageAction.Concur => "Concurred", StageAction.NotConcur => "Did not concur",
        StageAction.Approve => "Approved", StageAction.Sign => "Signed", StageAction.Dispatch => "Dispatched", StageAction.Record => "Placed on record", StageAction.Reject => "Rejected",
        StageAction.Return => "Returned", _ => a.ToString(),
    };

    // ---- information marking, withdrawal, revision -------------------------------------------------------------
    public async Task<DocumentDetailDto> InformAsync(int id, InformRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var perms = await PermissionsAsync(me, d, ct);
        if (!perms.Inform) throw await audit.DenyAsync("document", "mark-for-information", me, "document", d.Id.ToString(), "Only the initiator or an officer now holding the document can mark it for information.", ct);
        var all = await h.UsersAsync(ct);
        var ids = (r.UserIds ?? []).Distinct().ToList();
        if (ids.Count == 0) throw AppException.Bad("Choose at least one officer.");
        foreach (var uid in ids) if (!all.TryGetValue(uid, out var u) || !u.Active) throw AppException.Bad($"{uid} is not an active user.");
        var step = d.Route.Where(s => s.Status == StageStatus.Active && s.Blocking).Select(s => s.Step).DefaultIfEmpty(d.Route.Count == 0 ? 1 : d.Route.Max(s => s.Step)).Min();
        foreach (var uid in ids)
        {
            var s = new RouteStage { DocumentId = d.Id, Step = step, Name = "For information", Type = StageType.Information, UserId = uid, AdHoc = true, Status = StageStatus.Active, ReceivedAt = DateTime.UtcNow, VersionId = d.ActiveVersionId, DueAt = DateTime.UtcNow.AddHours(d.DocumentType!.StageServiceHours) };
            d.Route.Add(s);
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ToUserId = uid, StageName = s.Name, Action = "Marked for information", ActionRequested = "For information — not an approval", Comment = r.Note, VersionNo = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId)?.No });
        }
        notify.Notify(ids, "route", $"For information: {d.Number}", $"{d.Subject}" + (r.Note is null ? "" : $" — {r.Note}"), "document", d.Id.ToString(), me.Id);
        audit.Log("document", "mark-for-information", me, "document", d.Id.ToString(), next: new { ids }, reason: r.Note);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DocumentDetailDto> WithdrawAsync(int id, string? reason, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "document-withdraw", ct);
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Bad("Give the reason for withdrawal.", "reason");
        if (d.Status != DocumentStatus.InRoute) throw AppException.Rule("document-status", "Only a document on its route can be withdrawn.");
        if (d.Route.Any(s => s.Status == StageStatus.Done && s.Type is StageType.Approval or StageType.Signature))
            throw AppException.Rule("withdraw-after-approval", "An approval or signature is already recorded. Ask the officer now holding the document to return it, or use Revise.");
        foreach (var s in d.Route.Where(s => s.Status is StageStatus.Active or StageStatus.Pending)) s.Status = StageStatus.Cancelled;
        var holders = d.Route.Where(s => s.Status == StageStatus.Cancelled && s.ReceivedAt != null).Select(s => s.UserId).ToList();
        d.Status = DocumentStatus.Withdrawn; d.Outcome = "Withdrawn"; d.CompletedAt = DateTime.UtcNow;
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ActingForId = actingFor, Action = "Withdrawn", Comment = reason });
        notify.Notify(holders, "decision", $"Withdrawn: {d.Number}", reason, "document", d.Id.ToString(), me.Id);
        audit.Log("document", "document-withdrawn", me, "document", d.Id.ToString(), reason: reason, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>A material revision after approval (BRD 6): the new file becomes a draft version, approvals lapse,
    /// signatures on the superseded version are invalidated, and the document comes back to the initiator for re-routing.</summary>
    public async Task<DocumentDetailDto> ReviseAsync(int id, string fileName, string? contentType, byte[] bytes, string? reason, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var actingFor = await RequireInitiatorAsync(me, d, "document-revise", ct);
        if (string.IsNullOrWhiteSpace(reason)) throw AppException.Bad("Give the reason for the revision.", "reason");
        var perms = await PermissionsAsync(me, d, ct);
        if (!perms.Revise) throw AppException.Rule("document-status", "Revise applies to a document that is approved, signed or completed. Before approval, ask for it to be returned instead.");
        var approvers = d.Route.Where(s => s.Status == StageStatus.Done && s.Type is StageType.Approval or StageType.Signature or StageType.Concurrence).Select(s => s.UserId).ToList();
        foreach (var s in d.Route) if (s.Status == StageStatus.Active) s.Status = s.AdHoc ? StageStatus.Cancelled : StageStatus.Pending;
        var wasSigned = false;
        foreach (var f in d.SignatureFields.Where(f => f.Status is SignatureFieldStatus.Signed or SignatureFieldStatus.Pending).ToList())
        {
            wasSigned |= f.Status == SignatureFieldStatus.Signed;
            f.Status = SignatureFieldStatus.Invalidated; f.InvalidatedReason = $"Superseded by a revision on {DateTime.UtcNow:dd-MM-yyyy}: {reason}";
            // the placement carries over for re-signing
            d.SignatureFields.Add(new SignatureField { DocumentId = d.Id, RouteStageId = f.RouteStageId, SignerUserId = f.SignerUserId, Order = f.Order, Page = f.Page, X = f.X, Y = f.Y, W = f.W, H = f.H, ShowName = f.ShowName, ShowDate = f.ShowDate });
        }
        var v = await AddVersionAsync(d, me, fileName, contentType, bytes, "Revision: " + reason, ct);
        d.Status = DocumentStatus.Returned; d.ApprovalState = ApprovalState.Pending; d.SignatureState = wasSigned ? SignatureState.Invalidated : d.SignatureState == SignatureState.NotRequired ? SignatureState.NotRequired : SignatureState.Pending;
        d.CompletedAt = null; d.Outcome = null;
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, ActingForId = actingFor, Action = "Revised after approval", Comment = $"{reason}. Approvals lapsed{(wasSigned ? " and signatures invalidated" : "")}; re-routing required.", VersionNo = v.No });
        notify.Notify(approvers, "route", $"Revised after your approval: {d.Number}", $"v{v.No} supersedes the version you acted on. {reason}", "document", d.Id.ToString(), me.Id);
        audit.Log("document", "revised-after-approval", me, "document", d.Id.ToString(), next: new { Version = v.No, SignaturesInvalidated = wasSigned }, reason: reason, actingForId: actingFor);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---- signing ---------------------------------------------------------------------------------------------
    public async Task<DocumentDetailDto> SignAsync(int id, int stageId, SignRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var s = d.Route.FirstOrDefault(x => x.Id == stageId) ?? throw AppException.NotFound("Stage");
        if (s.Type != StageType.Signature) throw AppException.Bad("This stage is not a signature stage.");
        await CheckStageAsync(me, d, s, StageAction.Sign, r.VersionId, ct);
        var (_, actingFor) = await StageActorAsync(me, d, s, ct);
        if (!r.Consent) throw AppException.Bad("Confirm that you are signing the version shown, under your own authority or the delegation recorded.", "consent");
        // identity re-confirmation at the moment of signing
        var fresh = await db.Users.FirstAsync(u => u.Id == me.Id, ct);
        if (!hasher.Verify(r.Password ?? "", fresh.PasswordHash, fresh.PasswordSalt))
        {
            audit.Log("signature", "sign-reauth-failed", me, "document", d.Id.ToString(), reason: "wrong password at signing", result: "denied", actingForId: actingFor);
            await db.SaveChangesAsync(ct);
            throw new AppException(401, "Your password was not accepted. The document was not signed.", "reauth");
        }
        if (d.ApprovalState == ApprovalState.Pending) throw AppException.Rule("not-approved", "The document is not yet approved; it cannot be signed.");
        var version = d.Versions.First(v => v.Id == d.ActiveVersionId);
        var bytes = await files.ReadAsync(version.File!, ct);
        var hash = Integrity.Sha256Hex(bytes);
        if (hash != version.File!.Sha256)
        {
            audit.Log("signature", "integrity-failed", me, "document", d.Id.ToString(), reason: $"stored hash {version.File.Sha256}, file now {hash}", result: "failed", actingForId: actingFor);
            await db.SaveChangesAsync(ct);
            throw AppException.Rule("integrity", "The approved file no longer matches its recorded fingerprint. Signing was stopped; the incident is logged.");
        }
        var now = DateTime.UtcNow;
        var fields = d.SignatureFields.Where(f => f.RouteStageId == s.Id && f.Status == SignatureFieldStatus.Pending).ToList();
        if (fields.Count == 0)
        {
            var f = new SignatureField { DocumentId = d.Id, RouteStageId = s.Id, SignerUserId = s.UserId!, Order = d.SignatureFields.Count + 1, Status = SignatureFieldStatus.Pending };
            d.SignatureFields.Add(f); fields.Add(f);
        }
        var all = await h.UsersAsync(ct);
        var signer = all[me.Id];
        var principal = actingFor is null ? null : all.GetValueOrDefault(actingFor);
        var stamps = new List<StampSpec>();
        foreach (var f in fields)
        {
            f.Status = SignatureFieldStatus.Signed; f.VersionId = version.Id; f.DocumentHash = hash; f.SignedAt = now; f.SignedById = me.Id; f.ActingForId = actingFor;
            f.AuthMethod = "password re-authentication"; f.SignatureCode = Integrity.NewCode();
            f.SignatureMac = Integrity.Seal(signingKey.Key, Integrity.SealPayload(d.Number, version.Id, hash, me.Id, actingFor, now, f.SignatureCode));
            var lines = new List<string> { "Electronically signed" };
            if (f.ShowName) { lines.Add(signer.Name); if (signer.Designation is not null) lines.Add(signer.Designation.Title); }
            if (principal is not null) lines.Add($"for {principal.Name}");
            if (f.ShowDate) lines.Add($"{now:dd-MM-yyyy HH:mm} UTC");
            lines.Add($"{d.Number} · v{version.No}");
            lines.Add($"Verification code {f.SignatureCode}");
            stamps.Add(new StampSpec(f.Page, f.X, f.Y, f.W, f.H, lines));
        }
        // stamps accumulate on the latest signed copy of this version; the approved original is never modified
        var baseFile = d.SignatureFields.Where(f => f.Status == SignatureFieldStatus.Signed && f.VersionId == version.Id && f.SignedFileId != null && !fields.Contains(f)).OrderByDescending(f => f.SignedAt).FirstOrDefault();
        var baseBytes = baseFile is null ? bytes : await files.ReadAsync(await db.Files.FirstAsync(x => x.Id == baseFile.SignedFileId, ct), ct);
        var stamped = version.File.ContentType == "application/pdf" ? stamper.Stamp(baseBytes, stamps) : null;
        if (stamped is not null)
        {
            var sf = await files.StoreGeneratedAsync($"documents/{d.Id}/signed", Path.GetFileNameWithoutExtension(version.File.FileName) + $"-signed-{fields[0].SignatureCode}.pdf", "application/pdf", stamped, me.Id, ct);
            foreach (var f in fields) f.SignedFileId = sf.Id;
        }
        audit.Log("signature", "document-signed", me, "document", d.Id.ToString(), next: new { Stage = s.Name, Version = version.No, Hash = hash, Codes = fields.Select(f => f.SignatureCode), Stamped = stamped is not null }, actingForId: actingFor);

        // the last signature: the signed output becomes the active version before the route moves on (dispatch receives it)
        if (!d.Route.Any(x => x.Type == StageType.Signature && x.Id != s.Id && x.Status is StageStatus.Pending or StageStatus.Active))
        {
            var finalFileId = stamped is not null ? fields[0].SignedFileId!.Value
                : d.SignatureFields.Where(f => f.Status == SignatureFieldStatus.Signed && f.VersionId == version.Id && f.SignedFileId != null).OrderByDescending(f => f.SignedAt).Select(f => f.SignedFileId).FirstOrDefault() ?? version.FileId;
            var signedVersion = new DocumentVersion { No = d.Versions.Max(v => v.No) + 1, FileId = finalFileId, State = VersionState.Signed, Note = $"Signed output of v{version.No}", CreatedById = me.Id };
            d.Versions.Add(signedVersion);
            await db.SaveChangesAsync(ct);
            d.ActiveVersionId = signedVersion.Id; d.SignatureState = SignatureState.Signed; version.State = VersionState.Approved;
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, Action = "Signed output recorded", Comment = $"v{signedVersion.No} — signed copy of v{version.No}", VersionNo = signedVersion.No });
        }
        await ApplyActionAsync(d, s, me, actingFor, StageAction.Sign, null, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<VerificationDto> VerifyAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var d = await LoadVisibleAsync(me, id, ct, tracking: false);
        var all = await h.UsersAsync(ct);
        var checks = new List<SignatureCheckDto>();
        foreach (var f in d.SignatureFields.Where(f => f.SignatureMac != null).OrderBy(f => f.SignedAt))
        {
            var v = d.Versions.FirstOrDefault(x => x.Id == f.VersionId);
            var unchanged = false; var intact = true;
            if (v?.File is not null)
                try { var hsh = Integrity.Sha256Hex(await files.ReadAsync(v.File, ct)); unchanged = hsh == f.DocumentHash && hsh == v.File.Sha256; } catch (AppException) { unchanged = false; }
            if (f.SignedFileId is Guid sid)
            {
                var sf = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, ct);
                try { intact = sf is not null && Integrity.Sha256Hex(await files.ReadAsync(sf, ct)) == sf.Sha256; } catch (AppException) { intact = false; }
            }
            var seal = Integrity.VerifySeal(signingKey.Key, Integrity.SealPayload(d.Number, f.VersionId ?? 0, f.DocumentHash ?? "", f.SignedById ?? "", f.ActingForId, DateTime.SpecifyKind(f.SignedAt ?? default, DateTimeKind.Utc), f.SignatureCode ?? ""), f.SignatureMac);
            var verdict = f.Status == SignatureFieldStatus.Invalidated ? "Invalidated — " + f.InvalidatedReason
                : seal && unchanged && intact ? "Valid" : "NOT VALID — " + string.Join(", ", new[] { seal ? null : "seal does not match", unchanged ? null : "signed version altered or missing", intact ? null : "stamped copy altered or missing" }.Where(x => x != null));
            checks.Add(new SignatureCheckDto(f.Id, all.GetValueOrDefault(f.SignedById ?? "")?.Name ?? f.SignedById ?? "", f.ActingForId is null ? null : all.GetValueOrDefault(f.ActingForId)?.Name, f.SignedAt, f.SignatureCode, seal, unchanged, intact, verdict));
        }
        var live = checks.Where(c => !c.Verdict.StartsWith("Invalidated")).ToList();
        var overall = live.Count == 0 ? (checks.Count == 0 ? "No signatures" : "No current signatures") : live.All(c => c.Verdict == "Valid") ? "All current signatures valid" : "Verification FAILED";
        audit.Log("signature", "signatures-verified", me, "document", d.Id.ToString(), next: new { overall, Count = checks.Count }, result: overall.Contains("FAILED") ? "failed" : "ok");
        await db.SaveChangesAsync(ct);
        return new VerificationDto(d.Id, d.Number, DateTime.UtcNow, overall, checks, SignatureMechanism);
    }

    /// <summary>Look up a signature by the code printed on a stamp.</summary>
    public async Task<VerificationDto> VerifyCodeAsync(string code, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var c = (code ?? "").Trim().ToUpperInvariant();
        var f = await db.SignatureFields.AsNoTracking().FirstOrDefaultAsync(x => x.SignatureCode == c, ct) ?? throw AppException.NotFound("Signature code");
        return await VerifyAsync(f.DocumentId, ct);
    }

    // ---- route amendments after submission ---------------------------------------------------------------------
    async Task<bool> CanDecideAmendmentAsync(User me, RouteAmendment a, CancellationToken ct)
    {
        if (a.RequestedById == me.Id) return false;
        if (AccessPolicy.Can(me, Permissions.Workflow)) return true;
        var requesterBoss = (await h.UsersAsync(ct)).GetValueOrDefault(a.RequestedById)?.ReportsToId;
        return requesterBoss is not null && (await h.PrincipalsAsync(me.Id, DelegationScope.Documents, null, ct)).Contains(requesterBoss);
    }

    public async Task<DocumentDetailDto> RequestAmendmentAsync(int id, AmendmentRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var perms = await PermissionsAsync(me, d, ct);
        if (!perms.Amend) throw await audit.DenyAsync("workflow", "route-amendment-requested", me, "document", d.Id.ToString(), "Only the initiator, the officer now holding the document, or a workflow administrator can amend a submitted route.", ct);
        if (string.IsNullOrWhiteSpace(r.Reason)) throw AppException.Bad("Give the reason for the route change.", "reason");
        if (r.Ops is null || r.Ops.Count == 0) throw AppException.Bad("Describe at least one change.");
        await ValidateOpsAsync(d, r.Ops, me, ct);
        var a = new RouteAmendment { DocumentId = d.Id, RequestedById = me.Id, Reason = r.Reason.Trim(), ProposalJson = JsonSerializer.Serialize(r.Ops, ConfigService.Json), BeforeJson = JsonSerializer.Serialize(Snapshot(d), ConfigService.Json) };
        d.Amendments.Add(a);
        var needsApproval = d.DocumentType!.AmendmentNeedsApproval && !AccessPolicy.Can(me, Permissions.Workflow);
        audit.Log("workflow", "route-amendment-requested", me, "document", d.Id.ToString(), next: r.Ops, reason: a.Reason);
        if (needsApproval)
        {
            var all = await h.UsersAsync(ct);
            var deciders = all.Values.Where(u => u.Active && u.Role?.Has(Permissions.Workflow) == true).Select(u => (string?)u.Id).Append(all.GetValueOrDefault(me.Id)?.ReportsToId);
            await db.SaveChangesAsync(ct);
            notify.Notify(deciders, "route", $"Route amendment for approval: {d.Number}", $"{me.Name}: {a.Reason}", "document", d.Id.ToString(), me.Id);
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, Action = "Route amendment requested", Comment = a.Reason });
        }
        else await ApplyAmendmentAsync(d, a, me, "Applied without separate approval (" + (AccessPolicy.Can(me, Permissions.Workflow) ? "workflow administrator" : "not required for this document type") + ")", ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<DocumentDetailDto> DecideAmendmentAsync(int id, int amendmentId, AmendmentDecision r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var d = await LoadVisibleAsync(me, id, ct);
        var a = d.Amendments.FirstOrDefault(x => x.Id == amendmentId) ?? throw AppException.NotFound("Amendment");
        if (a.Status != "Pending") throw AppException.Rule("amendment-decided", $"This amendment is already {a.Status.ToLower()}.");
        if (!await CanDecideAmendmentAsync(me, a, ct)) throw await audit.DenyAsync("workflow", "route-amendment-decided", me, "document", d.Id.ToString(), "Only a workflow administrator or the requester's reporting officer can decide this amendment, and not the requester.", ct);
        if (!r.Approve)
        {
            if (string.IsNullOrWhiteSpace(r.Note)) throw AppException.Bad("Give the reason for refusing the amendment.", "reason");
            a.Status = "Rejected"; a.DecidedById = me.Id; a.DecidedAt = DateTime.UtcNow; a.DecisionNote = r.Note;
            db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = me.Id, Action = "Route amendment refused", Comment = r.Note });
            notify.Notify([a.RequestedById], "route", $"Route amendment refused: {d.Number}", r.Note, "document", d.Id.ToString(), me.Id);
            audit.Log("workflow", "route-amendment-rejected", me, "document", d.Id.ToString(), reason: r.Note);
        }
        else
        {
            var ops = JsonSerializer.Deserialize<List<AmendmentOp>>(a.ProposalJson, ConfigService.Json) ?? [];
            await ValidateOpsAsync(d, ops, null, ct);
            await ApplyAmendmentAsync(d, a, me, r.Note, ct);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    async Task<List<RouteLine>> ValidateOpsAsync(DocumentCase d, List<AmendmentOp> ops, User? requester, CancellationToken ct)
    {
        if (d.Status is not (DocumentStatus.InRoute or DocumentStatus.Returned)) throw AppException.Rule("document-status", "Only a submitted document on its route can have its route amended.");
        var all = await h.UsersAsync(ct);
        var template = d.TemplateId is int tid ? await db.WorkflowTemplates.AsNoTracking().Include(t => t.Stages).FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        var lines = d.Route.Where(s => s.Status != StageStatus.Cancelled && !s.AdHoc).ToDictionary(s => s.Id, s => new RouteLine(s.Step, s.Name, s.Type, s.UserId, s.Mandatory, s.TemplateStageId));
        var added = new List<RouteLine>();
        var currentStep = d.Route.Where(s => s.Status == StageStatus.Active && s.Blocking).Select(s => s.Step).DefaultIfEmpty(1).Min();
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case "remove":
                    {
                        var s = d.Route.FirstOrDefault(x => x.Id == op.StageId) ?? throw AppException.Bad("Unknown stage in the amendment.");
                        if (s.Mandatory) throw AppException.Rule("MANDATORY_STAGE", $"\"{s.Name}\" is mandatory and cannot be removed (FR-013).");
                        if (s.Status != StageStatus.Pending) throw AppException.Rule("stage-status", $"\"{s.Name}\" is {s.Status}; only pending stages can be removed.");
                        lines.Remove(s.Id); break;
                    }
                case "reassign":
                    {
                        var s = d.Route.FirstOrDefault(x => x.Id == op.StageId) ?? throw AppException.Bad("Unknown stage in the amendment.");
                        if (s.Status is not (StageStatus.Pending or StageStatus.Active)) throw AppException.Rule("stage-status", $"\"{s.Name}\" is {s.Status}; it can no longer be reassigned.");
                        if (string.IsNullOrWhiteSpace(op.UserId) || !all.TryGetValue(op.UserId, out var nu) || !nu.Active) throw AppException.Bad("Choose an active officer.");
                        if (requester is not null && !AccessPolicy.Can(requester, Permissions.Workflow) && s.TemplateStageId is int ts && template?.Stages.FirstOrDefault(x => x.Id == ts) is { InitiatorMayChangeAssignee: false })
                            throw AppException.Rule("ASSIGNEE_FIXED", $"The officer for \"{s.Name}\" is fixed by the template; a workflow administrator must make this change.");
                        lines[s.Id] = lines[s.Id] with { UserId = op.UserId }; break;
                    }
                case "add":
                    {
                        if (op.Type is not StageType t) throw AppException.Bad("Choose the stage type to add.");
                        if (string.IsNullOrWhiteSpace(op.UserId) || !all.ContainsKey(op.UserId)) throw AppException.Bad("Choose the officer for the new stage.");
                        var step = op.Step ?? currentStep + 1;
                        if (d.Status == DocumentStatus.InRoute && step < currentStep) throw AppException.Rule("step-passed", $"Step {step} is already behind the document; add stages at step {currentStep} or later.");
                        if (template is { AllowCustomization: false } && requester is not null && !AccessPolicy.Can(requester, Permissions.Workflow)) throw AppException.Rule("CUSTOMIZATION_NOT_ALLOWED", "This template does not allow additional stages.");
                        added.Add(new RouteLine(step, string.IsNullOrWhiteSpace(op.Name) ? StageRules.Requested(t) : op.Name.Trim(), t, op.UserId, false, null)); break;
                    }
                default: throw AppException.Bad($"Unknown amendment operation \"{op.Op}\".");
            }
        }
        var proposed = lines.Values.Concat(added).OrderBy(l => l.Step).ToList();
        var issues = (await ValidateAsync(d, ct, proposed)).Where(i => i.IsError && i.Code != "NO_ATTACHMENT").ToList();
        if (issues.Count > 0) throw AppException.Rule("route-invalid", "The amended route would be invalid: " + string.Join(" ", issues.Select(i => i.Message)), issues);
        return proposed;
    }

    async Task ApplyAmendmentAsync(DocumentCase d, RouteAmendment a, User decider, string? note, CancellationToken ct)
    {
        var ops = JsonSerializer.Deserialize<List<AmendmentOp>>(a.ProposalJson, ConfigService.Json) ?? [];
        var all = await h.UsersAsync(ct);
        var now = DateTime.UtcNow;
        var currentStep = d.Route.Where(s => s.Status == StageStatus.Active && s.Blocking).Select(s => s.Step).DefaultIfEmpty(int.MaxValue).Min();
        var affected = new List<string?>();
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case "remove":
                    var gone = d.Route.First(x => x.Id == op.StageId);
                    affected.Add(gone.UserId);
                    db.SignatureFields.RemoveRange(d.SignatureFields.Where(f => f.RouteStageId == gone.Id));
                    db.RouteStages.Remove(gone); d.Route.Remove(gone); break;
                case "reassign":
                    var s = d.Route.First(x => x.Id == op.StageId);
                    var old = s.UserId;
                    s.UserId = op.UserId; affected.Add(old); affected.Add(op.UserId);
                    foreach (var f in d.SignatureFields.Where(f => f.RouteStageId == s.Id && f.Status is SignatureFieldStatus.Draft or SignatureFieldStatus.Pending)) f.SignerUserId = op.UserId!;
                    if (s.Status == StageStatus.Active)
                    {
                        s.ReceivedAt = now; s.DueAt = now.AddHours(s.ServiceHours ?? d.DocumentType!.StageServiceHours); s.EscalationLevel = 0; s.RemindedAt = null;
                        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = old, ToUserId = op.UserId, RouteStageId = s.Id, StageName = s.Name, Action = "Reassigned by amendment", ActionRequested = StageRules.Requested(s.Type), Comment = a.Reason, VersionNo = d.Versions.FirstOrDefault(v => v.Id == d.ActiveVersionId)?.No });
                        notify.Notify([op.UserId], "route", $"{StageRules.Requested(s.Type)}: {d.Number}", $"{d.Subject} (reassigned to you: {a.Reason})", "document", d.Id.ToString());
                    }
                    break;
                case "add":
                    var ns = new RouteStage { Step = op.Step ?? (currentStep == int.MaxValue ? 1 : currentStep + 1), Name = string.IsNullOrWhiteSpace(op.Name) ? StageRules.Requested(op.Type!.Value) : op.Name.Trim(), Type = op.Type!.Value, UserId = op.UserId };
                    d.Route.Add(ns); affected.Add(op.UserId);
                    if (d.Status == DocumentStatus.InRoute && ns.Step == currentStep) { await db.SaveChangesAsync(ct); await ActivateAsync(d, ns, decider.Id, now, all, ct); }
                    break;
            }
        }
        a.Status = "Applied"; a.DecidedById = decider.Id; a.DecidedAt = now; a.DecisionNote = note; a.AfterJson = JsonSerializer.Serialize(Snapshot(d), ConfigService.Json);
        db.Movements.Add(new Movement { DocumentId = d.Id, FromUserId = decider.Id, Action = "Route amended", Comment = a.Reason + (note is null ? "" : $" — {note}") });
        notify.Notify([d.InitiatorId, a.RequestedById, .. affected], "route", $"Route amended: {d.Number}", a.Reason, "document", d.Id.ToString(), decider.Id);
        audit.Log("workflow", "route-amended", decider, "document", d.Id.ToString(), a.BeforeJson, a.AfterJson, a.Reason);
        if (d.Status == DocumentStatus.InRoute) { await db.SaveChangesAsync(ct); await ActivateNextAsync(d, decider.Id, ct); }
    }
}
