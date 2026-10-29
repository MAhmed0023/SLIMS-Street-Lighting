using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Services;
using Pera.Domain.Entities;
using Pera.Infrastructure.Persistence;

namespace Pera.Tests;

/// <summary>The acceptance scenarios of FRD section 7, run through the real services. Users come from the seeded
/// illustrative hierarchy: adg → dir-ops → dd-ops → (ad-ops1, ad-ops2, so-ops); adg → dir-comp → ad-comp; legal; rnd.</summary>
public class AcceptanceTests
{
    static byte[] Pdf(string text) => Seeder.MinimalPdf("Summary", [text, "Body of the summary.", "", "For approval."]);
    static DateTime Today => DateTime.UtcNow.Date;

    static async Task<DocumentDetailDto> NewNote(TestHost h, string subject = "Summary for approval", Classification cls = Classification.Internal, string initiator = "so-ops")
    {
        var types = await h.Run<WorkflowAdminService, List<DocumentTypeDto>>(initiator, s => s.TypesAsync(default));
        var note = types.First(t => t.Code == "NOTE");
        var d = await h.Run<DocumentService, DocumentDetailDto>(initiator, s => s.CreateAsync(new DocumentUpsert(subject, note.Id, null, cls, null, null, null, null), default));
        return await h.Run<DocumentService, DocumentDetailDto>(initiator, s => s.UploadVersionAsync(d.Document.Id, "summary.pdf", "application/pdf", Pdf("v1 " + subject), "first draft", default));
    }

    static Task<DocumentDetailDto> Submit(TestHost h, int id, string who = "so-ops") => h.Run<DocumentService, DocumentDetailDto>(who, s => s.SubmitAsync(id, new SubmitRequest(true), default));
    static Task<DocumentDetailDto> Get(TestHost h, int id, string who) => h.Run<DocumentService, DocumentDetailDto>(who, s => s.GetAsync(id, default));

    static async Task<DocumentDetailDto> Act(TestHost h, string who, int id, string stageType, StageAction action, string? comment = null, int? versionId = null)
    {
        var d = await Get(h, id, who);
        var stage = d.Route.First(r => r.Type == stageType && r.Status == "Active");
        var v = versionId ?? d.Versions.First(x => x.IsActive).Id;
        return await h.Run<DocumentService, DocumentDetailDto>(who, s => s.ActAsync(id, stage.Id, new StageActionRequest(action, comment, v), default));
    }

    static async Task<DocumentDetailDto> Sign(TestHost h, string who, int id, string password = TestHost.Password)
    {
        var d = await Get(h, id, who);
        var stage = d.Route.First(r => r.Type == "Signature" && r.Status == "Active");
        return await h.Run<DocumentService, DocumentDetailDto>(who, s => s.SignAsync(id, stage.Id, new SignRequest(password, d.Versions.First(x => x.IsActive).Id, true), default));
    }

    static async Task<DocumentDetailDto> ToSignature(TestHost h, int id)
    {
        await Act(h, "dd-ops", id, "Review", StageAction.Recommend, "Recommended.");
        await Act(h, "legal", id, "Concurrence", StageAction.Concur, "No legal objection.");
        return await Act(h, "adg", id, "Approval", StageAction.Approve, "Approved.");
    }

    // ---- FRD 7.1 ------------------------------------------------------------------------------------------
    [Fact]
    public async Task Adg_task_with_two_subtasks_shows_on_the_adg_dashboard_with_overdue_risk()
    {
        await using var h = await TestHost.StartAsync();
        var parent = await h.Run<TaskService, TaskDetailDto>("adg", s => s.CreateAsync(new TaskUpsert("Prepare drive plan", "Q3 drive", null, TaskPriority.High, "dir-ops", null, Today.AddDays(10), "Approved plan", null, null, false, true), default));
        Assert.Equal("Assigned", parent.Task.Status);
        await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.ActAsync(parent.Task.Id, "start", new TaskActionRequest(null, null), default));
        var withKids = await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.AddSubTasksAsync(parent.Task.Id, new SubTasksRequest(
        [
            new("Compile inspection data", "dd-ops", Today.AddDays(4), "Data sheet", TaskPriority.Normal, null),
            new("Field rosters", "ad-ops1", Today.AddDays(5), "Roster", TaskPriority.Normal, null),
        ]), default));
        Assert.Equal(2, withKids.SubTasks.Count);
        Assert.All(withKids.SubTasks, c => Assert.Equal("Assigned", c.Status));
        Assert.All(withKids.SubTasks, c => Assert.Equal(parent.Task.Id, c.ParentId));

        // one sub-task slips past its date
        using (var s = h.As("adg")) { var kid = await s.Db.Tasks.FirstAsync(t => t.OwnerId == "dd-ops"); kid.DueOn = Today.AddDays(-3); await s.Db.SaveChangesAsync(); }

        var dash = await h.Run<DashboardService, DashboardDto>("adg", s => s.GetAsync(default));
        Assert.Equal(3, dash.TaskRisk.First(c => c.Key == "open").Count);
        Assert.Equal(1, dash.TaskRisk.First(c => c.Key == "overdue").Count);
        Assert.Contains(dash.OwnerLoad, o => o.UserId == "dd-ops" && o.Overdue == 1);
        var adgView = await h.Run<TaskService, TaskDetailDto>("adg", s => s.GetAsync(parent.Task.Id, default));
        Assert.Contains(adgView.SubTasks, c => c.OwnerId == "dd-ops" && c.Overdue && c.OverdueDays == 3);

        // another branch cannot see it; the owner of a sub-task cannot assign outside their line
        await Assert.ThrowsAsync<AppException>(() => h.Run<TaskService, TaskDetailDto>("ad-comp", s => s.GetAsync(parent.Task.Id, default)));
        var e = await Assert.ThrowsAsync<AppException>(() => h.Run<TaskService, TaskDetailDto>("dd-ops", s => s.CreateAsync(new TaskUpsert("x", null, null, TaskPriority.Normal, "ad-comp", null, Today.AddDays(2), "y", null, null, false, true), default)));
        Assert.Equal(403, e.Status);
    }

    // ---- FRD 7.2 ------------------------------------------------------------------------------------------
    [Fact]
    public async Task A_blocker_and_its_evidence_are_visible_to_the_manager_and_the_adg_with_its_age()
    {
        await using var h = await TestHost.StartAsync();
        var t = await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.CreateAsync(new TaskUpsert("Deployment schedule", null, null, TaskPriority.Normal, "ad-ops1", null, Today.AddDays(6), "Schedule", null, null, false, true), default));
        await h.Run<TaskService, TaskDetailDto>("ad-ops1", s => s.ActAsync(t.Task.Id, "start", new TaskActionRequest(null, null), default));
        await Assert.ThrowsAsync<AppException>(() => h.Run<TaskService, TaskDetailDto>("ad-ops1", s => s.ActAsync(t.Task.Id, "block", new TaskActionRequest(null, null), default)));
        await h.Run<TaskService, TaskDetailDto>("ad-ops1", s => s.ActAsync(t.Task.Id, "block", new TaskActionRequest("Vehicles not released by Admin", null), default));
        await h.Run<TaskService, TaskDetailDto>("ad-ops1", s => s.AttachAsync(t.Task.Id, "vehicle-request.pdf", "application/pdf", Pdf("request"), "evidence", "Request letter", default));
        using (var s = h.As("ad-ops1")) { var row = await s.Db.Tasks.FirstAsync(x => x.Id == t.Task.Id); row.BlockedSince = DateTime.UtcNow.AddDays(-2).AddMinutes(-5); await s.Db.SaveChangesAsync(); }

        foreach (var viewer in new[] { "dd-ops", "dir-ops", "adg" })
        {
            var v = await h.Run<TaskService, TaskDetailDto>(viewer, s => s.GetAsync(t.Task.Id, default));
            Assert.Equal("Blocked", v.Task.Status);
            Assert.Equal("Vehicles not released by Admin", v.Blocker);
            Assert.Equal(2, v.Task.BlockedDays);
            Assert.Single(v.Attachments, a => a.FileName == "vehicle-request.pdf" && a.Stage == "Blocked");
        }
        var dash = await h.Run<DashboardService, DashboardDto>("adg", s => s.GetAsync(default));
        Assert.Contains(dash.BlockedTasks, b => b.Id == t.Task.Id);
        Assert.True(await h.Run<NotificationService, int>("dir-ops", s => s.UnreadAsync(default)) > 0);
    }

    [Fact]
    public async Task Closure_needs_review_and_closed_subtasks_and_keeps_the_record()
    {
        await using var h = await TestHost.StartAsync();
        var p = await h.Run<TaskService, TaskDetailDto>("adg", s => s.CreateAsync(new TaskUpsert("Parent", null, null, TaskPriority.Normal, "dir-ops", null, Today.AddDays(9), "Report", null, null, false, true), default));
        await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.ActAsync(p.Task.Id, "start", new TaskActionRequest(null, null), default));
        await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.AddSubTasksAsync(p.Task.Id, new SubTasksRequest([new("Child", "dd-ops", Today.AddDays(5), "Input", TaskPriority.Normal, null)]), default));
        await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.ActAsync(p.Task.Id, "submit", new TaskActionRequest("Done", null), default));
        // the owner cannot close their own output; the closure authority can't close while a sub-task is open
        await Assert.ThrowsAsync<AppException>(() => h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.ActAsync(p.Task.Id, "close", new TaskActionRequest(null, null), default)));
        var e = await Assert.ThrowsAsync<AppException>(() => h.Run<TaskService, TaskDetailDto>("adg", s => s.ActAsync(p.Task.Id, "close", new TaskActionRequest(null, null), default)));
        Assert.Equal("open-subtasks", e.Step);
        var returned = await h.Run<TaskService, TaskDetailDto>("adg", s => s.ActAsync(p.Task.Id, "return", new TaskActionRequest("Add the district figures", null), default));
        Assert.Equal("Returned", returned.Task.Status);
        Assert.Equal(1, returned.ReturnCount);
        // reassignment keeps the history
        var child = returned.SubTasks[0];
        var re = await h.Run<TaskService, TaskDetailDto>("dir-ops", s => s.ActAsync(child.Id, "reassign", new TaskActionRequest("dd-ops on leave", "ad-ops2"), default));
        Assert.Equal("ad-ops2", re.Task.OwnerId);
        Assert.Contains(re.Activity, a => a.Event == "reassigned" && a.OldValue == "Deputy Director Operations" && a.Comment == "dd-ops on leave");
    }

    // ---- FRD 7.3 ------------------------------------------------------------------------------------------
    [Fact]
    public async Task Removing_a_mandatory_approval_stage_is_blocked_and_explained()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        Assert.Equal(5, d.Route.Count);
        Assert.Equal(["dd-ops", "legal", "adg", "adg", "rnd"], d.Route.OrderBy(r => r.Step).Select(r => r.UserId).ToArray());
        var lines = d.Route.Where(r => r.Type != "Approval").Select(r => new RouteLineRequest(r.Id, r.Step, r.Name, Enum.Parse<StageType>(r.Type), r.UserId)).ToList();
        var e = await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.SetRouteAsync(d.Document.Id, new RouteRequest(lines), default)));
        Assert.Equal(409, e.Status);
        Assert.Equal("MANDATORY_MISSING", e.Step);
        Assert.Contains("cannot be removed", e.Message);
        // nor can the fixed approver be swapped
        var swapped = d.Route.Select(r => new RouteLineRequest(r.Id, r.Step, r.Name, Enum.Parse<StageType>(r.Type), r.Type == "Approval" ? "dir-ops" : r.UserId)).ToList();
        var e2 = await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.SetRouteAsync(d.Document.Id, new RouteRequest(swapped), default)));
        Assert.Equal("ASSIGNEE_FIXED", e2.Step);
        // an optional stage may go, and a new one may be added
        var trimmed = d.Route.Where(r => r.Type != "Concurrence").Select(r => new RouteLineRequest(r.Id, r.Step, r.Name, Enum.Parse<StageType>(r.Type), r.UserId)).Append(new RouteLineRequest(null, 1, "Second review", StageType.Review, "ad-ops1")).ToList();
        var ok = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.SetRouteAsync(d.Document.Id, new RouteRequest(trimmed), default));
        Assert.Equal(5, ok.Route.Count);
        Assert.DoesNotContain(ok.RouteIssues, i => i.IsError);
    }

    // ---- FRD 7.4 and 7.5 ------------------------------------------------------------------------------------
    [Fact]
    public async Task Return_new_version_keeps_history_and_a_superseded_version_cannot_be_approved()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        var v1 = d.Versions.Single().Id;
        await Submit(h, id);
        var back = await Act(h, "dd-ops", id, "Review", StageAction.Return, "Para 3 needs the vehicle numbers.");
        Assert.Equal("Returned", back.Document.Status);
        // only the initiator revises
        await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("dd-ops", s => s.UploadVersionAsync(id, "x.pdf", "application/pdf", Pdf("x"), null, default)));
        var v2doc = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.UploadVersionAsync(id, "summary-v2.pdf", "application/pdf", Pdf("v2"), "Vehicle numbers added", default));
        await Submit(h, id);
        var now = await Get(h, id, "so-ops");
        Assert.Equal(2, now.Versions.Count);
        Assert.Equal("Superseded", now.Versions.Single(v => v.No == 1).State);
        Assert.True(now.Versions.Single(v => v.No == 2).IsActive);
        Assert.Contains(now.Movements, m => m.Action == "Returned for revision" && m.Comment == "Para 3 needs the vehicle numbers." && m.VersionNo == 1);
        Assert.Contains(now.Movements, m => m.Action == "Resubmitted" && m.VersionNo == 2);
        Assert.Equal("Active", now.Route.First(r => r.Type == "Review").Status);

        // acting on the superseded version is refused and logged
        var e = await Assert.ThrowsAsync<AppException>(() => Act(h, "dd-ops", id, "Review", StageAction.Recommend, "ok", versionId: v1));
        Assert.Equal(409, e.Status);
        Assert.Equal("superseded-version", e.Step);
        using var s = h.As("audit");
        Assert.True(await s.Db.AuditLogs.AnyAsync(a => a.EntityId == id.ToString() && a.Result == "denied" && a.ActorId == "dd-ops"));
    }

    // ---- FRD 7.6 -------------------------------------------------------------------------------------------
    [Fact]
    public async Task Signing_the_approved_version_produces_a_verifiable_signed_output()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        var approved = await ToSignature(h, id);
        Assert.Equal("Approved", approved.Document.ApprovalState);
        Assert.Contains(approved.Can.Stages, x => x.StageType == "Signature") ;

        // wrong password: nothing is signed, the attempt is logged
        var bad = await Assert.ThrowsAsync<AppException>(() => Sign(h, "adg", id, "wrong-password1"));
        Assert.Equal(401, bad.Status);
        var signed = await Sign(h, "adg", id);
        Assert.Equal("Signed", signed.Document.SignatureState);
        var field = Assert.Single(signed.SignatureFields);
        Assert.Equal("Signed", field.Status);
        Assert.NotNull(field.SignatureCode);
        var active = signed.Versions.First(v => v.IsActive);
        Assert.Equal("Signed", active.State);
        Assert.Equal("Approved", signed.Versions.First(v => v.No == 1).State);

        // the stamped copy is a new PDF; the approved original is untouched
        var (bytes, name, type) = await h.Run<DocumentService, (byte[], string, string)>("so-ops", s => s.DownloadSignedAsync(id, field.Id, default));
        Assert.Equal("application/pdf", type);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.NotEqual(active.Sha256, signed.Versions.First(v => v.No == 1).Sha256);

        var done = await Act(h, "rnd", id, "Dispatch", StageAction.Dispatch);
        Assert.Equal("Completed", done.Document.Status);
        Assert.Equal("Dispatched", done.Document.Outcome);

        var verify = await h.Run<DocumentService, VerificationDto>("audit", s => s.VerifyAsync(id, default));
        Assert.Equal("All current signatures valid", verify.Overall);
        Assert.True(verify.Signatures.Single().SealValid);
        var byCode = await h.Run<DocumentService, VerificationDto>("so-ops", s => s.VerifyCodeAsync(field.SignatureCode!, default));
        Assert.Equal(id, byCode.DocumentId);
        // a code does not open a document to someone with no access to it
        await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, VerificationDto>("dir-comp", s => s.VerifyCodeAsync(field.SignatureCode!, default)));

        // tamper with the approved file on disk: verification fails
        using (var s = h.As("audit"))
        {
            var path = (await s.Db.DocumentVersions.Include(v => v.File).FirstAsync(v => v.DocumentId == id && v.No == 1)).File!.Path;
            await File.WriteAllBytesAsync(Path.Combine(h.Uploads, path), Pdf("altered"));
        }
        var after = await h.Run<DocumentService, VerificationDto>("audit", s => s.VerifyAsync(id, default));
        Assert.Equal("Verification FAILED", after.Overall);
        Assert.False(after.Signatures.Single().DocumentUnchanged);
    }

    // ---- FRD 7.7 -------------------------------------------------------------------------------------------
    [Fact]
    public async Task Approval_without_authority_is_denied_and_logged()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        await Act(h, "dd-ops", id, "Review", StageAction.Recommend, "ok");
        await Act(h, "legal", id, "Concurrence", StageAction.Concur, "ok");

        // someone else entirely
        var stranger = await Assert.ThrowsAsync<AppException>(async () =>
        {
            var doc = await Get(h, id, "audit");
            var st = doc.Route.First(r => r.Type == "Approval");
            await h.Run<DocumentService, DocumentDetailDto>("dir-comp", s => s.ActAsync(id, st.Id, new StageActionRequest(StageAction.Approve, "ok", doc.Versions.First(v => v.IsActive).Id), default));
        });
        Assert.True(stranger.Status is 403 or 404);

        // the named approver, after the delegation of powers is withdrawn
        using (var s = h.As("wfadmin")) { foreach (var r in await s.Db.AuthorityRules.Include(r => r.Designation).Where(r => r.Designation!.Code == "ADG" && r.Authority == AuthorityKind.Approve).ToListAsync()) r.Active = false; await s.Db.SaveChangesAsync(); }
        var e = await Assert.ThrowsAsync<AppException>(() => Act(h, "adg", id, "Approval", StageAction.Approve, "ok"));
        Assert.Equal(403, e.Status);
        Assert.Equal("authority", e.Step);
        using var a = h.As("audit");
        Assert.True(await a.Db.AuditLogs.AnyAsync(x => x.EntityId == id.ToString() && x.Result == "denied" && x.ActorId == "adg" && x.Action == "stage-approve"));
        Assert.Equal("Active", (await Get(h, id, "adg")).Route.First(r => r.Type == "Approval").Status);
    }

    [Fact]
    public async Task An_acting_officer_approves_under_delegation_and_both_names_are_recorded()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        await Act(h, "dd-ops", id, "Review", StageAction.Recommend, "ok");
        await Act(h, "legal", id, "Concurrence", StageAction.Concur, "ok");
        await Assert.ThrowsAsync<AppException>(() => Act(h, "dir-comp", id, "Approval", StageAction.Approve, "ok"));
        await h.Run<OrganizationService, DelegationDto>("sysadmin", s => s.CreateDelegationAsync(new DelegationRequest("adg", "dir-comp", DelegationScope.Approvals, null, Today, Today.AddDays(7), "ADG on leave — order no. 12"), default));
        var after = await Act(h, "dir-comp", id, "Approval", StageAction.Approve, "Approved for ADG.");
        var st = after.Route.First(r => r.Type == "Approval");
        Assert.Equal("Done", st.Status);
        Assert.Equal("Director Regulatory Compliance", st.ActedByName);
        Assert.Equal("ADG Implementation", st.ActingForName);
        Assert.Contains(after.Movements, m => m.Action == "Approved" && m.ActingForName == "ADG Implementation");
        // approval power does not carry signing power
        await Assert.ThrowsAsync<AppException>(() => Sign(h, "dir-comp", id));
        // the system administrator holds no approval power at all
        await Assert.ThrowsAsync<AppException>(() => Sign(h, "sysadmin", id));
    }

    // ---- FRD 7.8 -------------------------------------------------------------------------------------------
    [Fact]
    public async Task An_overdue_document_is_reminded_then_escalated_once_per_level()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        using (var s = h.As("so-ops")) { var st = await s.Db.RouteStages.FirstAsync(r => r.DocumentId == id && r.Status == StageStatus.Active); st.DueAt = DateTime.UtcNow.AddHours(5); await s.Db.SaveChangesAsync(); }
        var r1 = await h.Run<AutomationService, AutomationService.RunResult>("sysadmin", s => s.RunAsync(default));
        Assert.Equal(1, r1.Reminders);
        Assert.Equal(0, r1.Escalations);

        // 30 hours past the target: first escalation to the holder's reporting officer
        var late = DateTime.UtcNow.AddHours(35);
        var r2 = await h.Run<AutomationService, AutomationService.RunResult>("sysadmin", s => s.RunAsync(default, late));
        Assert.Equal(1, r2.Escalations);
        var again = await h.Run<AutomationService, AutomationService.RunResult>("sysadmin", s => s.RunAsync(default, late));
        Assert.Equal(0, again.Escalations);
        var r3 = await h.Run<AutomationService, AutomationService.RunResult>("sysadmin", s => s.RunAsync(default, DateTime.UtcNow.AddHours(80)));
        Assert.Equal(1, r3.Escalations);

        using var a = h.As("audit");
        var logs = await a.Db.EscalationLogs.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([0, 1, 2], logs.Select(l => l.Level).ToArray());
        Assert.Contains("dir-ops", logs[1].Recipients);          // dd-ops reports to dir-ops
        Assert.Contains("adg", logs[2].Recipients);              // and one level up
        Assert.True(await a.Db.Notifications.AnyAsync(n => n.UserId == "dir-ops" && n.Kind == "escalation"));
        Assert.True(await a.Db.AuditLogs.AnyAsync(x => x.Kind == "escalation" && x.Action == "escalated-level-1"));
    }

    // ---- FRD 7.9 -------------------------------------------------------------------------------------------
    [Fact]
    public async Task An_auditor_can_reconstruct_the_whole_sequence()
    {
        await using var h = await TestHost.StartAsync();
        var t = await h.Run<TaskService, TaskDetailDto>("adg", s => s.CreateAsync(new TaskUpsert("Summary for the drive", null, null, TaskPriority.Normal, "dir-ops", null, Today.AddDays(5), "Approved summary", null, null, false, true), default));
        var d = await NewNote(h);
        var id = d.Document.Id;
        await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.UpdateAsync(id, new DocumentUpsert(d.Document.Subject, d.Document.DocumentTypeId, null, Classification.Internal, t.Task.Id, null, null, null), default));
        await Submit(h, id);
        await Act(h, "dd-ops", id, "Review", StageAction.Recommend, "ok");
        await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.RequestAmendmentAsync(id, new AmendmentRequest("Finance view needed", [new AmendmentOp("add", null, 2, "Finance review", StageType.Review, "dir-comp")]), default));
        await Act(h, "legal", id, "Concurrence", StageAction.Concur, "ok");
        await Act(h, "adg", id, "Approval", StageAction.Approve, "ok");
        await Sign(h, "adg", id);
        await Act(h, "rnd", id, "Dispatch", StageAction.Record);

        var trail = await h.Run<AuditService, List<AuditDto>>("audit", s => s.ListAsync(null, null, "document", id.ToString(), null, null, null, null, 500, default));
        var actions = trail.Select(a => a.Action).ToHashSet();
        foreach (var expected in new[] { "document-registered", "version-uploaded", "route-submitted", "stage-recommend", "route-amendment-requested", "stage-concur", "stage-approve", "document-signed", "stage-record" })
            Assert.Contains(expected, actions);
        var taskTrail = await h.Run<AuditService, List<AuditDto>>("audit", s => s.ListAsync(null, null, "task", t.Task.Id.ToString(), null, null, null, null, 500, default));
        Assert.Contains(taskTrail, a => a.Action == "task-created");

        var doc = await Get(h, id, "audit");
        Assert.Equal("Completed", doc.Document.Status);
        Assert.Contains(doc.Amendments, a => a.Status == "Pending");
        var moves = doc.Movements.Select(m => m.Action).ToList();
        Assert.Equal("Registered", moves[0]);
        Assert.True(moves.IndexOf("Submitted") < moves.IndexOf("Approved") && moves.IndexOf("Approved") < moves.IndexOf("Signed"));

        var report = await h.Run<ReportService, ReportResult>("audit", s => s.RunAsync("movement", new ReportQuery(null, null, null, null, null, null, null), default));
        Assert.True(report.Rows.Count >= 8);
        Assert.Contains("Filters", ReportService.Csv(report));
        var (xlsx, name, _) = await h.Run<ReportService, (byte[], string, string)>("audit", s => s.ExportAsync("signatures", new ReportQuery(null, null, null, null, null, null, null), "xlsx", default));
        Assert.EndsWith(".xlsx", name);
        Assert.Equal((byte)'P', xlsx[0]);
        // a manager cannot open the full audit report
        await Assert.ThrowsAsync<AppException>(() => h.Run<ReportService, ReportResult>("dir-ops", s => s.RunAsync("audit", new ReportQuery(null, null, null, null, null, null, null), default)));
    }

    // ---- further controls ------------------------------------------------------------------------------------
    [Fact]
    public async Task Revision_after_signing_invalidates_the_signatures_and_needs_re_approval()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        await ToSignature(h, id);
        await Sign(h, "adg", id);
        await Act(h, "rnd", id, "Dispatch", StageAction.Dispatch);
        var revised = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.ReviseAsync(id, "summary-v3.pdf", "application/pdf", Pdf("material change"), "Figures corrected after dispatch", default));
        Assert.Equal("Returned", revised.Document.Status);
        Assert.Equal("Pending", revised.Document.ApprovalState);
        Assert.Equal("Invalidated", revised.Document.SignatureState);
        Assert.Contains(revised.SignatureFields, f => f.Status == "Invalidated" && f.InvalidatedReason!.Contains("Figures corrected"));
        Assert.Contains(revised.SignatureFields, f => f.Status == "Draft");
        var verify = await h.Run<DocumentService, VerificationDto>("so-ops", s => s.VerifyAsync(id, default));
        Assert.StartsWith("Invalidated", verify.Signatures.Single().Verdict);
        // the route runs again from the start on the new version
        var again = await Submit(h, id);
        Assert.Equal("Active", again.Route.First(r => r.Type == "Review").Status);
        Assert.Equal(again.Versions.First(v => v.IsActive).No, again.Route.First(r => r.Type == "Review").VersionNo);
    }

    [Fact]
    public async Task Restricted_documents_are_seen_only_by_participants()
    {
        await using var h = await TestHost.StartAsync();
        var open = await NewNote(h, "Internal matter");
        var secret = await NewNote(h, "Restricted matter", Classification.Restricted);
        var supervisorSees = await h.Run<DocumentService, List<DocumentDto>>("dir-ops", s => s.ListAsync("all", null, null, null, null, null, null, null, null, default));
        Assert.Contains(supervisorSees, x => x.Id == open.Document.Id);
        Assert.DoesNotContain(supervisorSees, x => x.Id == secret.Document.Id);
        await Assert.ThrowsAsync<AppException>(() => Get(h, secret.Document.Id, "dir-ops"));
        await Assert.ThrowsAsync<AppException>(() => Get(h, open.Document.Id, "ad-comp"));
        // the route officers are participants
        Assert.Equal(secret.Document.Id, (await Get(h, secret.Document.Id, "legal")).Document.Id);
        Assert.Equal(secret.Document.Id, (await Get(h, secret.Document.Id, "audit")).Document.Id);
    }

    [Fact]
    public async Task A_route_amendment_needs_approval_by_someone_other_than_the_requester()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        var req = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.RequestAmendmentAsync(id, new AmendmentRequest("Operations view needed", [new AmendmentOp("add", null, 2, "Operations review", StageType.Review, "ad-ops1")]), default));
        var am = Assert.Single(req.Amendments);
        Assert.Equal("Pending", am.Status);
        Assert.False(am.CanDecide);
        await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.DecideAmendmentAsync(id, am.Id, new AmendmentDecision(true, null), default)));
        var applied = await h.Run<DocumentService, DocumentDetailDto>("dd-ops", s => s.DecideAmendmentAsync(id, am.Id, new AmendmentDecision(true, "Agreed"), default));
        Assert.Equal("Applied", applied.Amendments.Single().Status);
        Assert.Contains(applied.Route, r => r.Name == "Operations review" && r.UserId == "ad-ops1");
        // a mandatory stage cannot be amended away
        var approval = applied.Route.First(r => r.Type == "Approval");
        var e = await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.RequestAmendmentAsync(id, new AmendmentRequest("skip", [new AmendmentOp("remove", approval.Id, null, null, null, null)]), default)));
        Assert.Equal("MANDATORY_STAGE", e.Step);
    }

    [Fact]
    public async Task Information_marking_is_acknowledged_and_never_counts_as_approval()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        var marked = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.InformAsync(id, new InformRequest(["dir-comp"], "For your awareness"), default));
        var info = marked.Route.Single(r => r.AdHoc);
        Assert.Equal("Active", info.Status);
        var ack = await h.Run<DocumentService, DocumentDetailDto>("dir-comp", s => s.ActAsync(id, info.Id, new StageActionRequest(StageAction.Acknowledge, null, marked.Versions.First(v => v.IsActive).Id), default));
        Assert.Equal("Done", ack.Route.Single(r => r.AdHoc).Status);
        Assert.Equal("Pending", ack.Document.ApprovalState);
        Assert.Equal("Active", ack.Route.First(r => r.Type == "Review").Status);
        // an information holder cannot approve
        await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("dir-comp", s => s.ActAsync(id, info.Id, new StageActionRequest(StageAction.Approve, null, marked.Versions.First(v => v.IsActive).Id), default)));
    }

    [Fact]
    public async Task Uploads_are_checked_for_type_content_size_and_malware()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        Task Up(string name, byte[] bytes) => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.UploadVersionAsync(id, name, null, bytes, null, default));
        Assert.Equal("file-type", (await Assert.ThrowsAsync<AppException>(() => Up("run.exe", [0x4D, 0x5A]))).Step);
        Assert.Equal("file-type", (await Assert.ThrowsAsync<AppException>(() => Up("fake.pdf", "not a pdf"u8.ToArray()))).Step);
        Assert.Equal("file-scan", (await Assert.ThrowsAsync<AppException>(() => Up("eicar.pdf", Pdf("x")))).Step);
        Assert.Equal("file-size", (await Assert.ThrowsAsync<AppException>(() => Up("big.pdf", [.. "%PDF"u8.ToArray(), .. new byte[21 * 1024 * 1024]]))).Step);
    }

    [Fact]
    public async Task A_withdrawal_is_refused_once_an_approval_is_recorded()
    {
        await using var h = await TestHost.StartAsync();
        var d = await NewNote(h);
        var id = d.Document.Id;
        await Submit(h, id);
        await ToSignature(h, id);
        var e = await Assert.ThrowsAsync<AppException>(() => h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.WithdrawAsync(id, "No longer needed", default)));
        Assert.Equal("withdraw-after-approval", e.Step);
        var d2 = await NewNote(h, "Second");
        await Submit(h, d2.Document.Id);
        var w = await h.Run<DocumentService, DocumentDetailDto>("so-ops", s => s.WithdrawAsync(d2.Document.Id, "Superseded by a fresh summary", default));
        Assert.Equal("Withdrawn", w.Document.Status);
        Assert.All(w.Route, r => Assert.Equal("Cancelled", r.Status));
    }
}
