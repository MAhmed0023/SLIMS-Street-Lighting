using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pera.Application.Interfaces;
using Pera.Application.Services;
using Pera.Domain.Config;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Infrastructure.Persistence;

/// <summary>First-start data: roles, parameters, an illustrative hierarchy (to be replaced by the approved PERA hierarchy,
/// designation list and delegation of powers — BRD 11), document types, workflow templates, the authority matrix and a
/// small demonstration set of tasks and draft documents. Runs only on an empty database; never overwrites live data.</summary>
public static class Seeder
{
    public static async Task RunAsync(AppDbContext db, IFileStorage storage, ILogger log, bool demo, CancellationToken ct = default)
    {
        if (db.Database.IsSqlServer()) await db.Database.MigrateAsync(ct); else await db.Database.EnsureCreatedAsync(ct);
        if (!await db.Roles.AnyAsync(ct)) { db.Roles.AddRange(DefaultConfig.DefaultRoles()); await db.SaveChangesAsync(ct); }
        if (!await db.AppSettings.AnyAsync(x => x.Key == "config", ct)) { db.AppSettings.Add(new AppSetting { Key = "config", Json = JsonSerializer.Serialize(DefaultConfig.Create(), ConfigService.Json) }); await db.SaveChangesAsync(ct); }
        if (await db.Users.AnyAsync(ct)) return;

        // ---- designations and units ----------------------------------------------------------------------
        Designation D(string code, string title, int rank) => new() { Code = code, Title = title, Rank = rank };
        var dDG = D("DG", "Director General", 1); var dADG = D("ADG", "Additional Director General", 2); var dDIR = D("DIR", "Director", 3);
        var dLEG = D("LEGAL", "Legal Advisor", 4); var dDD = D("DD", "Deputy Director", 5); var dAD = D("AD", "Assistant Director", 6);
        var dSO = D("SO", "Section Officer", 7); var dAST = D("AST", "Assistant / Dealing Hand", 8); var dSYS = D("SYS", "System Administrator", 9); var dAUD = D("AUD", "Internal Auditor", 6);
        db.Designations.AddRange(dDG, dADG, dDIR, dLEG, dDD, dAD, dSO, dAST, dSYS, dAUD);
        OrgUnit U(string code, string name, string kind, OrgUnit? parent) => new() { Code = code, Name = name, Kind = kind, Parent = parent };
        var hq = U("HQ", "PERA Headquarters", "Office", null);
        var impl = U("IMPL", "Implementation Wing", "Wing", hq);
        var ops = U("OPS", "Enforcement Operations Section", "Section", impl);
        var comp = U("COMP", "Regulatory Compliance Section", "Section", impl);
        var legal = U("LEGAL", "Legal Wing", "Wing", hq);
        var admin = U("ADMIN", "Administration & Receipt/Dispatch", "Section", hq);
        var it = U("IT", "IT & MIS Section", "Section", hq);
        db.OrgUnits.AddRange(hq, impl, ops, comp, legal, admin, it);
        await db.SaveChangesAsync(ct);

        // ---- accounts (no passwords: the first sysadmin sign-in sets the administrator password) -----------
        User P(string id, string name, string role, Designation d, OrgUnit u, string? reportsTo) => new() { Id = id, Name = name, RoleKey = role, DesignationId = d.Id, OrgUnitId = u.Id, ReportsToId = reportsTo, Active = true };
        db.Users.AddRange(
            P("sysadmin", "System Administrator", "sysadmin", dSYS, it, null),
            P("dg", "Director General", "adg", dDG, hq, null));
        await db.SaveChangesAsync(ct);
        db.Users.Add(P("adg", "ADG Implementation", "adg", dADG, impl, "dg"));
        db.Users.Add(P("legal", "Legal Advisor", "officer", dLEG, legal, "dg"));
        db.Users.Add(P("wfadmin", "Workflow Administrator", "workflowadmin", dSYS, it, "dg"));
        db.Users.Add(P("audit", "Internal Audit", "auditor", dAUD, hq, "dg"));
        db.Users.Add(P("rnd", "Receipt & Dispatch", "officer", dAST, admin, "dg"));
        await db.SaveChangesAsync(ct);
        db.Users.Add(P("dir-ops", "Director Enforcement Operations", "manager", dDIR, ops, "adg"));
        db.Users.Add(P("dir-comp", "Director Regulatory Compliance", "manager", dDIR, comp, "adg"));
        await db.SaveChangesAsync(ct);
        db.Users.Add(P("dd-ops", "Deputy Director Operations", "manager", dDD, ops, "dir-ops"));
        db.Users.Add(P("ad-comp", "Assistant Director Compliance", "officer", dAD, comp, "dir-comp"));
        await db.SaveChangesAsync(ct);
        db.Users.Add(P("ad-ops1", "Assistant Director Operations (North)", "officer", dAD, ops, "dd-ops"));
        db.Users.Add(P("ad-ops2", "Assistant Director Operations (South)", "officer", dAD, ops, "dd-ops"));
        db.Users.Add(P("so-ops", "Section Officer Operations", "officer", dSO, ops, "dd-ops"));
        await db.SaveChangesAsync(ct);
        impl.HeadUserId = "adg"; ops.HeadUserId = "dir-ops"; comp.HeadUserId = "dir-comp"; legal.HeadUserId = "legal"; hq.HeadUserId = "dg";
        db.TaskCategories.AddRange(new TaskCategory { Name = "Enforcement drive" }, new TaskCategory { Name = "Policy & regulation" }, new TaskCategory { Name = "Administration" },
            new TaskCategory { Name = "Legal matter" }, new TaskCategory { Name = "IT & systems" }, new TaskCategory { Name = "Reporting" });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Provisioned the illustrative PERA hierarchy and {count} accounts (no passwords; first sysadmin sign-in sets the administrator password)", await db.Users.CountAsync(ct));

        // ---- document types, templates, authority matrix -------------------------------------------------
        DocumentType T(string code, string name, bool requireApproval = true, Classification cls = Classification.Internal) => new() { Code = code, Name = name, AllowedExtensions = "pdf,docx,xlsx,jpg,png", RequireApprovalStage = requireApproval, DefaultClassification = cls };
        var tNote = T("NOTE", "Summary / note for approval"); var tLtr = T("LTR", "Official letter"); var tOrd = T("ORD", "Office order / notification", cls: Classification.Confidential);
        var tRpt = T("RPT", "Report for information", requireApproval: false); tRpt.EnforceAuthority = false;
        db.DocumentTypes.AddRange(tNote, tLtr, tOrd, tRpt);
        await db.SaveChangesAsync(ct);
        WorkflowTemplateStage S(int step, string name, StageType type, bool mandatory, AssigneeMode mode, string? user = null, Designation? d = null, int? hours = null, bool change = true)
            => new() { Step = step, Name = name, Type = type, Mandatory = mandatory, AssigneeMode = mode, UserId = user, DesignationId = d?.Id, ServiceHours = hours, InitiatorMayChangeAssignee = change };
        WorkflowTemplate W(string code, string name, DocumentType t, bool custom, params WorkflowTemplateStage[] stages)
        { var w = new WorkflowTemplate { Code = code, Name = name, DocumentTypeId = t.Id, Status = "Active", OwnerId = "wfadmin", AllowCustomization = custom, EffectiveFrom = DateTime.UtcNow.Date.AddDays(-30) }; w.Stages.AddRange(stages); return w; }
        var wNote = W("NOTE-STD", "Summary — review, legal concurrence, ADG approval and signature", tNote, true,
            S(1, "Review by reporting officer", StageType.Review, false, AssigneeMode.ReportingOfficer, hours: 24),
            S(2, "Legal concurrence", StageType.Concurrence, false, AssigneeMode.Designation, d: dLEG, hours: 48),
            S(3, "Approval — ADG", StageType.Approval, true, AssigneeMode.Designation, d: dADG, hours: 48, change: false),
            S(4, "Signature — ADG", StageType.Signature, true, AssigneeMode.Designation, d: dADG, hours: 24, change: false),
            S(5, "Dispatch / place on record", StageType.Dispatch, false, AssigneeMode.User, user: "rnd", hours: 24));
        var wLtr = W("LTR-STD", "Letter — review, Director approval and signature, dispatch", tLtr, true,
            S(1, "Review by reporting officer", StageType.Review, true, AssigneeMode.ReportingOfficer, hours: 24),
            S(2, "Approval — Director", StageType.Approval, true, AssigneeMode.Designation, d: dDIR, hours: 48),
            S(3, "Signature — Director", StageType.Signature, true, AssigneeMode.Designation, d: dDIR, hours: 24),
            S(4, "Dispatch", StageType.Dispatch, true, AssigneeMode.User, user: "rnd", hours: 24));
        var wOrd = W("ORD-STD", "Office order — legal concurrence, DG approval and signature", tOrd, false,
            S(1, "Review by reporting officer", StageType.Review, true, AssigneeMode.ReportingOfficer, hours: 24),
            S(2, "Legal concurrence", StageType.Concurrence, true, AssigneeMode.Designation, d: dLEG, hours: 48, change: false),
            S(3, "Approval — Director General", StageType.Approval, true, AssigneeMode.Designation, d: dDG, hours: 72, change: false),
            S(4, "Signature — Director General", StageType.Signature, true, AssigneeMode.Designation, d: dDG, hours: 24, change: false),
            S(5, "Notification and dispatch", StageType.Dispatch, true, AssigneeMode.User, user: "rnd", hours: 24));
        var wRpt = W("RPT-STD", "Report — review, then ADG for information", tRpt, true,
            S(1, "Review by reporting officer", StageType.Review, false, AssigneeMode.ReportingOfficer, hours: 48),
            S(2, "ADG — for information", StageType.Information, false, AssigneeMode.Designation, d: dADG, hours: 72));
        db.WorkflowTemplates.AddRange(wNote, wLtr, wOrd, wRpt);
        await db.SaveChangesAsync(ct);
        tNote.DefaultTemplateId = wNote.Id; tLtr.DefaultTemplateId = wLtr.Id; tOrd.DefaultTemplateId = wOrd.Id; tRpt.DefaultTemplateId = wRpt.Id;
        AuthorityRule A(Designation d, AuthorityKind k, DocumentType? t = null, string? note = null) => new() { DesignationId = d.Id, Authority = k, DocumentTypeId = t?.Id, EffectiveFrom = DateTime.UtcNow.Date.AddDays(-30), Note = note };
        db.AuthorityRules.AddRange(
            A(dDG, AuthorityKind.Approve, note: "All document types"), A(dDG, AuthorityKind.Sign, note: "All document types"),
            A(dADG, AuthorityKind.Approve, tNote), A(dADG, AuthorityKind.Sign, tNote), A(dADG, AuthorityKind.Approve, tLtr), A(dADG, AuthorityKind.Sign, tLtr),
            A(dDIR, AuthorityKind.Approve, tLtr), A(dDIR, AuthorityKind.Sign, tLtr), A(dDIR, AuthorityKind.Concur), A(dLEG, AuthorityKind.Concur));
        await db.SaveChangesAsync(ct);

        if (!demo) return;
        await DemoAsync(db, storage, tNote, wNote, ct);
        log.LogInformation("Seeded demonstration tasks and draft documents");
    }

    static async Task DemoAsync(AppDbContext db, IFileStorage storage, DocumentType tNote, WorkflowTemplate wNote, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var today = now.Date;
        var fy = Codes.FY(now);
        var seq = 0;
        var cats = await db.TaskCategories.ToDictionaryAsync(c => c.Name, c => c.Id, ct);
        WorkTask Task(string title, string by, string owner, int dueDays, WorkTaskStatus st, int progress, string deliverable, WorkTask? parent = null, string? cat = null, TaskPriority pr = TaskPriority.Normal)
        {
            var t = new WorkTask
            {
                Number = $"PERA/TSK/{fy}/{++seq:D5}", Title = title, AssignedById = by, CreatedById = by, OwnerId = owner, Status = st, Progress = progress, DueOn = today.AddDays(dueDays), Deliverable = deliverable,
                Parent = parent, Priority = pr, CategoryId = cat is null ? null : cats[cat], CreatedAt = now.AddDays(-12 + seq), AssignedAt = now.AddDays(-12 + seq),
                StartedAt = st is WorkTaskStatus.Assigned ? null : now.AddDays(-10 + seq),
            };
            t.Activities.Add(new TaskActivity { At = t.CreatedAt, ActorId = by, Event = "created", Stage = WorkTaskStatus.Draft, NewValue = title });
            t.Activities.Add(new TaskActivity { At = t.CreatedAt, ActorId = by, Event = "assigned", Stage = WorkTaskStatus.Assigned, NewValue = owner });
            if (st != WorkTaskStatus.Assigned) t.Activities.Add(new TaskActivity { At = t.StartedAt ?? now, ActorId = owner, Event = "status", Stage = WorkTaskStatus.InProgress, OldValue = "Assigned", NewValue = "In Progress" });
            db.Tasks.Add(t);
            return t;
        }
        var plan = Task("Prepare Q3 enforcement drive plan for Lahore division", "adg", "dir-ops", 10, WorkTaskStatus.InProgress, 40, "Approved drive plan with team deployment, targets and reporting format", cat: "Enforcement drive", pr: TaskPriority.High);
        plan.Purpose = "Coordinated enforcement against encroachment and price-control violations across the division."; plan.ProgressNote = "Inspection data being compiled; deployment schedule pending vehicles."; plan.NextAction = "Consolidate sub-task outputs";
        var data = Task("Compile last quarter's inspection data", "dir-ops", "dd-ops", 4, WorkTaskStatus.InProgress, 60, "Consolidated inspection data sheet by tehsil", plan, "Enforcement drive");
        var sched = Task("Draft deployment schedule for field teams", "dir-ops", "dd-ops", -2, WorkTaskStatus.Blocked, 30, "Week-wise team deployment schedule", plan, "Enforcement drive", TaskPriority.High);
        sched.Blocker = "Awaiting vehicle availability confirmation from the Administration section."; sched.BlockedSince = now.AddDays(-3);
        sched.Activities.Add(new TaskActivity { At = now.AddDays(-3), ActorId = "dd-ops", Event = "status", Stage = WorkTaskStatus.Blocked, OldValue = "In Progress", NewValue = "Blocked", Comment = sched.Blocker });
        var roster = Task("Collect field team rosters", "dd-ops", "ad-ops1", -6, WorkTaskStatus.Completed, 100, "Roster of officers available per week", sched, "Enforcement drive");
        roster.ClosedAt = now.AddDays(-5); roster.ClosedById = "dd-ops"; roster.ClosureNote = "Rosters received for all four teams.";
        Task("Verify vehicle log books for the drive", "dd-ops", "ad-ops2", 3, WorkTaskStatus.Assigned, 0, "Vehicle fitness and log book status note", sched, "Administration");
        Task("Review compliance SOP for licensed premises", "adg", "dir-comp", 14, WorkTaskStatus.Assigned, 0, "Revised SOP with tracked changes and a summary of changes", cat: "Policy & regulation");
        var notices = Task("Summarise pending show-cause notices", "dir-comp", "ad-comp", -5, WorkTaskStatus.InProgress, 50, "Notice-wise status with recommended action", cat: "Legal matter", pr: TaskPriority.Urgent);
        notices.EscalationLevel = 1; notices.RemindedAt = now.AddDays(-6);
        notices.Activities.Add(new TaskActivity { At = now.AddDays(-4), ActorId = "system", Event = "escalated", Stage = WorkTaskStatus.InProgress, NewValue = "Level 1", Comment = "Overdue by 1 day(s). Notified Director Regulatory Compliance." });
        var monthly = Task("Monthly progress report — previous month", "adg", "dir-ops", 1, WorkTaskStatus.SubmittedForReview, 100, "Monthly progress report against targets", cat: "Reporting");
        monthly.SubmittedAt = now.AddDays(-1);
        monthly.Activities.Add(new TaskActivity { At = now.AddDays(-1), ActorId = "dir-ops", Event = "status", Stage = WorkTaskStatus.SubmittedForReview, OldValue = "In Progress", NewValue = "Submitted for Review", Comment = "Report attached for review." });
        await db.SaveChangesAsync(ct);
        db.TaskDependencies.Add(new TaskDependency { TaskId = sched.Id, DependsOnId = data.Id });
        db.DocSequences.Add(new DocSequence { Key = $"PERA/TSK/{fy}", Next = seq + 1 });

        // ---- a draft summary with its working file and the template route, ready to submit ----------------
        var pdf = MinimalPdf("Summary for approval", ["Subject: Q3 enforcement drive plan, Lahore division", "", "1. Background: price-control and encroachment complaints rose in Q2.",
            "2. Proposal: four field teams, week-wise deployment, daily reporting.", "3. Resources: four vehicles and twenty-four officers for six weeks.", "", "Submitted for approval, please.", "", "", "Section Officer Operations"]);
        using var ms = new MemoryStream(pdf);
        var path = await storage.SaveAsync("documents/seed", "Q3-drive-summary.pdf", ms, ct);
        var file = new StoredFile { FileName = "Q3-drive-summary.pdf", ContentType = "application/pdf", Size = pdf.Length, Sha256 = Integrity.Sha256Hex(pdf), Path = path, UploadedById = "so-ops", UploadedAt = now.AddHours(-3) };
        var doc = new DocumentCase { Number = $"PERA/DOC/{fy}/00001", Subject = "Approval of the Q3 enforcement drive plan, Lahore division", DocumentTypeId = tNote.Id, OriginOrgUnitId = (await db.Users.FirstAsync(u => u.Id == "so-ops", ct)).OrgUnitId,
            Classification = Classification.Internal, TaskId = plan.Id, InitiatorId = "so-ops", CreatedAt = now.AddHours(-3), TemplateId = wNote.Id, TemplateVersion = wNote.Version };
        doc.Versions.Add(new DocumentVersion { No = 1, File = file, State = VersionState.Draft, CreatedById = "so-ops", CreatedAt = now.AddHours(-3), Note = "First draft" });
        var resolve = new Dictionary<int, string> { [1] = "dd-ops", [2] = "legal", [3] = "adg", [4] = "adg", [5] = "rnd" };
        foreach (var s in wNote.Stages.OrderBy(s => s.Step)) doc.Route.Add(new RouteStage { Step = s.Step, Name = s.Name, Type = s.Type, Mandatory = s.Mandatory, TemplateStageId = s.Id, UserId = resolve[s.Step], ServiceHours = s.ServiceHours });
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        doc.ActiveVersionId = doc.Versions[0].Id;
        var sign = doc.Route.First(r => r.Type == StageType.Signature);
        doc.SignatureFields.Add(new SignatureField { RouteStageId = sign.Id, SignerUserId = sign.UserId!, Order = 1, Page = 1, X = 0.56, Y = 0.74, W = 0.34, H = 0.1 });
        db.Movements.Add(new Movement { DocumentId = doc.Id, At = doc.CreatedAt, FromUserId = "so-ops", Action = "Registered", Comment = doc.Subject });
        db.Movements.Add(new Movement { DocumentId = doc.Id, At = doc.CreatedAt, FromUserId = "so-ops", Action = "New version", Comment = "v1: Q3-drive-summary.pdf — First draft", VersionNo = 1 });
        db.DocSequences.Add(new DocSequence { Key = $"PERA/DOC/{fy}", Next = 2 });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A one-page A4 PDF with Helvetica text, built by hand so the seeder needs no PDF library.</summary>
    public static byte[] MinimalPdf(string title, IEnumerable<string> lines)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var content = new StringBuilder();
        content.Append(CultureInfo.InvariantCulture, $"BT /F2 16 Tf 72 770 Td ({Esc(title)}) Tj ET\n");
        var y = 735;
        foreach (var l in lines) { content.Append(CultureInfo.InvariantCulture, $"BT /F1 11 Tf 72 {y} Td ({Esc(l)}) Tj ET\n"); y -= 18; }
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString())); sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(CultureInfo.InvariantCulture, $"{o:D10} 00000 n \n");
        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
