using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>Document types, versioned workflow templates (FR-012) and the authority matrix (BRD 11).
/// Maintained by workflow administrators; every change is audited (FR-028).</summary>
public class WorkflowAdminService(IAppDbContext db, UserContext users, AuditService audit, HierarchyService h)
{
    public static DocumentTypeDto ToDto(DocumentType t) => new(t.Id, t.Code, t.Name, t.AllowedExtensions, t.MaxSizeMb, t.RequireAttachment, t.EnforceAuthority, t.AllowSelfApproval,
        t.AllowParallel, t.AmendmentNeedsApproval, t.RequireApprovalStage, t.ApprovalCommentRequired, t.DefaultClassification.ToString(), t.StageServiceHours, t.DefaultTemplateId, t.Active);

    public async Task<List<DocumentTypeDto>> TypesAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return (await db.DocumentTypes.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<DocumentTypeDto> SaveTypeAsync(DocumentTypeUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Workflow, ct);
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Code and name are required.");
        if (r.MaxSizeMb is < 1 or > 200) throw AppException.Bad("Maximum size must be between 1 and 200 MB.");
        if (r.StageServiceHours is < 1 or > 2000) throw AppException.Bad("Stage turnaround must be between 1 and 2000 hours.");
        var t = r.Id is int id ? await db.DocumentTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Document type") : new DocumentType();
        if (await db.DocumentTypes.AnyAsync(x => x.Code == r.Code.Trim().ToUpper() && x.Id != t.Id, ct)) throw AppException.Bad($"Code {r.Code} is already used.");
        if (r.DefaultTemplateId is int dt && !await db.WorkflowTemplates.AnyAsync(x => x.Id == dt && x.DocumentTypeId == t.Id, ct)) throw AppException.Bad("The default template must belong to this document type.");
        var prev = t.Id == 0 ? null : ToDto(t);
        t.Code = r.Code.Trim().ToUpperInvariant(); t.Name = r.Name.Trim();
        t.AllowedExtensions = string.Join(',', r.AllowedExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.TrimStart('.').ToLowerInvariant()).Distinct());
        t.MaxSizeMb = r.MaxSizeMb; t.RequireAttachment = r.RequireAttachment; t.EnforceAuthority = r.EnforceAuthority; t.AllowSelfApproval = r.AllowSelfApproval; t.AllowParallel = r.AllowParallel;
        t.AmendmentNeedsApproval = r.AmendmentNeedsApproval; t.RequireApprovalStage = r.RequireApprovalStage; t.ApprovalCommentRequired = r.ApprovalCommentRequired;
        t.DefaultClassification = r.DefaultClassification; t.StageServiceHours = r.StageServiceHours; t.DefaultTemplateId = r.DefaultTemplateId; t.Active = r.Active;
        if (t.Id == 0) db.DocumentTypes.Add(t);
        await db.SaveChangesAsync(ct);
        audit.Log("config", prev is null ? "document-type-created" : "document-type-updated", me, "documenttype", t.Id.ToString(), prev, ToDto(t));
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    // ---- templates ----------------------------------------------------------------------------------------
    public async Task<List<TemplateDto>> TemplatesAsync(int? documentTypeId, bool includeRetired, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var q = db.WorkflowTemplates.AsNoTracking().Include(t => t.Stages).Include(t => t.DocumentType).AsQueryable();
        if (documentTypeId is int dt) q = q.Where(t => t.DocumentTypeId == dt);
        if (!includeRetired) q = q.Where(t => t.Status != "Retired");
        var rows = await q.OrderBy(t => t.Code).ThenByDescending(t => t.Version).ToListAsync(ct);
        var all = await h.UsersAsync(ct);
        var desig = await db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        return rows.Select(t => ToDto(t, all, desig)).ToList();
    }

    static TemplateDto ToDto(WorkflowTemplate t, Dictionary<string, User> all, Dictionary<int, string> desig) => new(t.Id, t.Code, t.Name, t.Version, t.DocumentTypeId, t.DocumentType?.Name ?? "", t.Status,
        t.EffectiveFrom, t.EffectiveTo, t.OwnerId, all.GetValueOrDefault(t.OwnerId)?.Name ?? t.OwnerId, t.AllowCustomization,
        t.Stages.OrderBy(s => s.Step).ThenBy(s => s.Id).Select(s => new TemplateStageDto(s.Id, s.Step, s.Name, s.Type.ToString(), s.Mandatory, s.AssigneeMode.ToString(), s.UserId,
            s.UserId is null ? null : all.GetValueOrDefault(s.UserId)?.Name, s.DesignationId, s.DesignationId is int d ? desig.GetValueOrDefault(d) : null, s.ServiceHours, s.InitiatorMayChangeAssignee)).ToList());

    static void Check(TemplateUpsert r)
    {
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Code and name are required.");
        if (r.Stages is null || r.Stages.Count == 0) throw AppException.Bad("A template needs at least one stage.");
        foreach (var s in r.Stages)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) throw AppException.Bad("Every stage needs a name.");
            if (s.Step < 1) throw AppException.Bad("Steps are numbered from 1.");
            if (s.AssigneeMode == AssigneeMode.User && string.IsNullOrWhiteSpace(s.UserId)) throw AppException.Bad($"Stage \"{s.Name}\" names a fixed officer but none is chosen.");
            if (s.AssigneeMode == AssigneeMode.Designation && s.DesignationId is null) throw AppException.Bad($"Stage \"{s.Name}\" routes by designation but none is chosen.");
        }
        var approvals = r.Stages.Where(s => s.Type == StageType.Approval).Select(s => s.Step).DefaultIfEmpty(0).Max();
        if (r.Stages.Any(s => s.Type == StageType.Signature && s.Step <= approvals)) throw AppException.Bad("Signature stages must come after every approval stage.");
    }

    /// <summary>Saves a draft template, or — for an active one — publishes the change as the next version and retires the
    /// previous version, so documents already submitted keep the route they were submitted with.</summary>
    public async Task<TemplateDto> SaveTemplateAsync(TemplateUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Workflow, ct);
        Check(r);
        if (!await db.DocumentTypes.AnyAsync(t => t.Id == r.DocumentTypeId, ct)) throw AppException.Bad("Unknown document type.");
        WorkflowTemplate t;
        string action;
        if (r.Id is int id)
        {
            var current = await db.WorkflowTemplates.Include(x => x.Stages).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Template");
            if (current.Status == "Retired") throw AppException.Rule("template-retired", "A retired version cannot be edited; edit the version in force.");
            if (current.Status == "Draft")
            {
                t = current; action = "template-draft-updated";
                db.WorkflowTemplateStages.RemoveRange(t.Stages); t.Stages.Clear();
            }
            else
            {
                t = new WorkflowTemplate { Code = current.Code, Version = current.Version + 1, Status = "Active", OwnerId = me.Id };
                current.Status = "Retired"; current.EffectiveTo = DateTime.UtcNow.Date;
                db.WorkflowTemplates.Add(t); action = "template-version-published";
            }
        }
        else
        {
            if (await db.WorkflowTemplates.AnyAsync(x => x.Code == r.Code.Trim().ToUpper(), ct)) throw AppException.Bad($"Template code {r.Code} is already used.");
            t = new WorkflowTemplate { Code = r.Code.Trim().ToUpperInvariant(), Version = 1, Status = "Draft", OwnerId = me.Id };
            db.WorkflowTemplates.Add(t); action = "template-created";
        }
        t.Name = r.Name.Trim(); t.DocumentTypeId = r.DocumentTypeId; t.AllowCustomization = r.AllowCustomization; t.EffectiveFrom = (r.EffectiveFrom ?? DateTime.UtcNow).Date;
        foreach (var s in r.Stages.OrderBy(s => s.Step))
            t.Stages.Add(new WorkflowTemplateStage { Step = s.Step, Name = s.Name.Trim(), Type = s.Type, Mandatory = s.Mandatory, AssigneeMode = s.AssigneeMode, UserId = s.AssigneeMode == AssigneeMode.User ? s.UserId : null,
                DesignationId = s.AssigneeMode == AssigneeMode.Designation ? s.DesignationId : null, ServiceHours = s.ServiceHours, InitiatorMayChangeAssignee = s.InitiatorMayChangeAssignee });
        await db.SaveChangesAsync(ct);
        // a document type pointing at the retired version follows to the new one
        if (action == "template-version-published")
            foreach (var dt in await db.DocumentTypes.Where(x => x.DefaultTemplateId == r.Id).ToListAsync(ct)) dt.DefaultTemplateId = t.Id;
        audit.Log("workflow", action, me, "template", t.Id.ToString(), next: new { t.Code, t.Version, t.Status, Stages = r.Stages });
        await db.SaveChangesAsync(ct);
        return (await TemplatesAsync(null, true, ct)).First(x => x.Id == t.Id);
    }

    public async Task<TemplateDto> SetTemplateStatusAsync(int id, string status, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Workflow, ct);
        var t = await db.WorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Template");
        if (status is not ("Active" or "Retired")) throw AppException.Bad("Status must be Active or Retired.");
        if (status == "Active")
        {
            if (t.Status != "Draft") throw AppException.Rule("template-status", "Only a draft can be activated.");
            foreach (var other in await db.WorkflowTemplates.Where(x => x.Code == t.Code && x.Id != t.Id && x.Status == "Active").ToListAsync(ct)) { other.Status = "Retired"; other.EffectiveTo = DateTime.UtcNow.Date; }
        }
        var prev = t.Status;
        t.Status = status; if (status == "Retired") t.EffectiveTo = DateTime.UtcNow.Date;
        audit.Log("workflow", "template-" + status.ToLower(), me, "template", t.Id.ToString(), new { Status = prev }, new { t.Status, t.Version });
        await db.SaveChangesAsync(ct);
        return (await TemplatesAsync(null, true, ct)).First(x => x.Id == t.Id);
    }

    // ---- authority matrix ------------------------------------------------------------------------------------
    public async Task<List<AuthorityRuleDto>> AuthorityAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return (await db.AuthorityRules.AsNoTracking().Include(r => r.Designation).Include(r => r.DocumentType).OrderBy(r => r.Designation!.Rank).ThenBy(r => r.Authority).ToListAsync(ct))
            .Select(r => new AuthorityRuleDto(r.Id, r.DocumentTypeId, r.DocumentType?.Name, r.DesignationId, r.Designation?.Title ?? "", r.Authority.ToString(), r.EffectiveFrom, r.EffectiveTo, r.Active, r.Note)).ToList();
    }

    public async Task<AuthorityRuleDto> SaveAuthorityAsync(AuthorityRuleUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Workflow, ct);
        if (!await db.Designations.AnyAsync(d => d.Id == r.DesignationId, ct)) throw AppException.Bad("Unknown designation.");
        if (r.DocumentTypeId is int t && !await db.DocumentTypes.AnyAsync(x => x.Id == t, ct)) throw AppException.Bad("Unknown document type.");
        if (r.EffectiveTo is DateTime e && e.Date < (r.EffectiveFrom ?? DateTime.UtcNow).Date) throw AppException.Bad("The rule ends before it starts.");
        var rule = r.Id is int id ? await db.AuthorityRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Rule") : new AuthorityRule();
        var prev = rule.Id == 0 ? null : new { rule.DocumentTypeId, rule.DesignationId, Authority = rule.Authority.ToString(), rule.EffectiveFrom, rule.EffectiveTo, rule.Active };
        rule.DocumentTypeId = r.DocumentTypeId; rule.DesignationId = r.DesignationId; rule.Authority = r.Authority; rule.EffectiveFrom = (r.EffectiveFrom ?? DateTime.UtcNow).Date; rule.EffectiveTo = r.EffectiveTo?.Date;
        rule.Active = r.Active; rule.Note = r.Note;
        if (rule.Id == 0) db.AuthorityRules.Add(rule);
        await db.SaveChangesAsync(ct);
        audit.Log("workflow", prev is null ? "authority-granted" : "authority-changed", me, "authority", rule.Id.ToString(), prev,
            new { rule.DocumentTypeId, rule.DesignationId, Authority = rule.Authority.ToString(), rule.EffectiveFrom, rule.EffectiveTo, rule.Active, rule.Note });
        await db.SaveChangesAsync(ct);
        return (await AuthorityAsync(ct)).First(x => x.Id == rule.Id);
    }
}
