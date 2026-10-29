using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Application.Services;
using Pera.Domain.Config;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Api.Controllers;

static class Http
{
    public static async Task<(byte[] Bytes, string Name, string? Type)> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw AppException.Bad("Choose a file to upload.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return (ms.ToArray(), file.FileName, file.ContentType);
    }
    public static FileContentResult Inline(ControllerBase c, byte[] bytes, string name, string type)
    {
        c.Response.Headers.ContentDisposition = new ContentDisposition { Inline = true, FileName = name }.ToString();
        c.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileContentResult(bytes, type);
    }
}

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, UserContext users, IConfiguration config) : ControllerBase
{
    const string RefreshCookie = "pera_refresh";
    bool Secure => config.GetValue("App:SecureCookies", false);

    void SetRefresh(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token, new CookieOptions { HttpOnly = true, Secure = Secure, SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = expires });
    TokenResponse ToResponse(AuthService.LoginResult r) { SetRefresh(r.RefreshToken, r.RefreshExpires); return new TokenResponse(r.AccessToken, r.AccessExpires, UserContext.ToDto(r.User), r.MustChange); }

    /// <summary>Sign in with user ID (or email) and password. First sign-in as sysadmin sets the administrator password.</summary>
    [HttpPost("login"), AllowAnonymous]
    public async Task<TokenResponse> Login([FromBody] LoginRequest r, CancellationToken ct) => ToResponse(await auth.LoginAsync(r.Id, r.Password, ct));

    /// <summary>Exchange the refresh cookie for a new access token (rotates the refresh token).</summary>
    [HttpPost("refresh"), AllowAnonymous]
    public async Task<TokenResponse> Refresh(CancellationToken ct) => ToResponse(await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct));

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct) { await auth.LogoutAsync(ct); Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" }); return Ok(new { ok = true }); }

    [HttpGet("me"), Authorize]
    public async Task<UserDto> Me(CancellationToken ct) => UserContext.ToDto(await users.RequireAsync(ct));

    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest r, CancellationToken ct) { await auth.ChangePasswordAsync(r.Old, r.New, ct); return Ok(new { ok = true }); }

    /// <summary>Names for the "Choose a user" picker on the sign-in card (empty when disabled in Parameters).</summary>
    [HttpGet("picker"), AllowAnonymous]
    public Task<List<UserPublicDto>> Picker(CancellationToken ct) => auth.PickerAsync(ct);
}

[ApiController]
[Route("api")]
public class MetaController(ConfigService configs, IAppDbContext db, UserContext users, AuditService audit) : ControllerBase
{
    [HttpGet("meta"), AllowAnonymous]
    public async Task<MetaDto> Meta(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        var firstRun = !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Users, u => u.PasswordHash != null, ct);
        return new MetaDto(cfg.App, cfg.LoginRequired, cfg.ShowUserPicker, firstRun, DateTime.UtcNow);
    }

    [HttpGet("config"), Authorize]
    public async Task<AppConfig> Config(CancellationToken ct) { await users.RequireAsync(ct); return await configs.GetAsync(ct); }

    /// <summary>Parameters: reminder and escalation thresholds, file rules, sign-in options (FR-028).</summary>
    [HttpPut("config"), Authorize]
    public async Task<AppConfig> Save([FromBody] AppConfig cfg, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        if (cfg.Alerts.Escalate2Hours < cfg.Alerts.Escalate1Hours) throw AppException.Bad("The second escalation must come after the first.");
        if (cfg.Alerts.ReminderHours < 0 || cfg.Alerts.Escalate1Hours < 0 || cfg.Alerts.WorkerMinutes < 1) throw AppException.Bad("Thresholds must be positive.");
        var prev = await configs.GetAsync(ct);
        cfg.Version = prev.Version + 1;
        await configs.SaveAsync(cfg, ct);
        audit.Log("config", "parameters-updated", me, "config", "config", new { prev.Alerts, prev.Files, prev.LoginRequired }, new { cfg.Alerts, cfg.Files, cfg.LoginRequired });
        await db.SaveChangesAsync(ct);
        return cfg;
    }
}

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(UsersService svc) : ControllerBase
{
    [HttpGet] public Task<List<UserDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpGet("roles")] public Task<List<RoleDto>> Roles(CancellationToken ct) => svc.RolesAsync(ct);
    [HttpPut] public Task<UserDto> Upsert([FromBody] UserUpsertRequest r, CancellationToken ct) => svc.UpsertAsync(r, ct);
    [HttpPost("{id}/password")] public async Task<IActionResult> SetPassword(string id, [FromBody] SetPasswordRequest r, CancellationToken ct) { await svc.SetPasswordAsync(id, r.Password, ct); return Ok(new { ok = true }); }
    [HttpPost("{id}/unlock")] public async Task<IActionResult> Unlock(string id, CancellationToken ct) { await svc.UnlockAsync(id, ct); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/org")]
[Authorize]
public class OrganizationController(OrganizationService svc) : ControllerBase
{
    /// <summary>Every active user, for pickers.</summary>
    [HttpGet("directory")] public Task<List<UserRefDto>> Directory([FromQuery] bool inactive, CancellationToken ct) => svc.DirectoryAsync(inactive, ct);
    /// <summary>The caller and everyone in their reporting line — who they may assign tasks to.</summary>
    [HttpGet("assignable")] public Task<List<UserRefDto>> Assignable(CancellationToken ct) => svc.AssignableAsync(ct);
    [HttpGet("units")] public Task<List<OrgUnitDto>> Units(CancellationToken ct) => svc.UnitsAsync(ct);
    [HttpPut("units")] public Task<OrgUnitDto> SaveUnit([FromBody] OrgUnitUpsert r, CancellationToken ct) => svc.SaveUnitAsync(r, ct);
    [HttpGet("designations")] public Task<List<DesignationDto>> Designations(CancellationToken ct) => svc.DesignationsAsync(ct);
    [HttpPut("designations")] public Task<DesignationDto> SaveDesignation([FromBody] DesignationUpsert r, CancellationToken ct) => svc.SaveDesignationAsync(r, ct);
    [HttpGet("categories")] public Task<List<CategoryDto>> Categories(CancellationToken ct) => svc.CategoriesAsync(ct);
    [HttpPut("categories")] public Task<CategoryDto> SaveCategory([FromBody] CategoryDto r, CancellationToken ct) => svc.SaveCategoryAsync(r, ct);
    [HttpGet("delegations")] public Task<List<DelegationDto>> Delegations(CancellationToken ct) => svc.DelegationsAsync(ct);
    [HttpPost("delegations")] public Task<DelegationDto> Delegate([FromBody] DelegationRequest r, CancellationToken ct) => svc.CreateDelegationAsync(r, ct);
    [HttpPost("delegations/{id:int}/revoke")] public async Task<IActionResult> Revoke(int id, [FromBody] ReasonRequest? r, CancellationToken ct) { await svc.RevokeDelegationAsync(id, r?.Reason, ct); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/tasks")]
[Authorize]
public class TasksController(TaskService svc) : ControllerBase
{
    /// <summary>scope = mine | assigned | team | review | all; status = a status, "open" or "overdue".</summary>
    [HttpGet]
    public Task<List<TaskDto>> List([FromQuery] string? scope, [FromQuery] string? status, [FromQuery] string? q, [FromQuery] string? ownerId, [FromQuery] string? priority,
        [FromQuery] int? parentId, [FromQuery] bool? topLevel, [FromQuery] DateTime? dueFrom, [FromQuery] DateTime? dueTo, [FromQuery] int? unitId, CancellationToken ct)
        => svc.ListAsync(scope, status, q, ownerId, priority, parentId, topLevel, dueFrom, dueTo, unitId, ct);
    [HttpGet("{id:int}")] public Task<TaskDetailDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost] public Task<TaskDetailDto> Create([FromBody] TaskUpsert r, CancellationToken ct) => svc.CreateAsync(r, ct);
    [HttpPut("{id:int}")] public Task<TaskDetailDto> Update(int id, [FromBody] TaskUpsert r, CancellationToken ct) => svc.UpdateAsync(id, r, ct);
    /// <summary>assign | start | block | unblock | submit | close | return | reopen | cancel | reassign. Reasons are required to block, return, reopen, cancel and reassign.</summary>
    [HttpPost("{id:int}/actions/{action}")] public Task<TaskDetailDto> Act(int id, string action, [FromBody] TaskActionRequest r, CancellationToken ct) => svc.ActAsync(id, action, r, ct);
    [HttpPost("{id:int}/progress")] public Task<TaskDetailDto> Progress(int id, [FromBody] TaskProgressRequest r, CancellationToken ct) => svc.ProgressAsync(id, r, ct);
    [HttpPost("{id:int}/comments")] public Task<TaskDetailDto> Comment(int id, [FromBody] CommentRequest r, CancellationToken ct) => svc.CommentAsync(id, r, ct);
    [HttpPost("{id:int}/subtasks")] public Task<TaskDetailDto> SubTasks(int id, [FromBody] SubTasksRequest r, CancellationToken ct) => svc.AddSubTasksAsync(id, r, ct);
    [HttpPost("{id:int}/dependencies")] public Task<TaskDetailDto> AddDependency(int id, [FromBody] DependencyRequest r, CancellationToken ct) => svc.AddDependencyAsync(id, r.DependsOnId, ct);
    [HttpDelete("{id:int}/dependencies/{dependsOnId:int}")] public Task<TaskDetailDto> RemoveDependency(int id, int dependsOnId, CancellationToken ct) => svc.RemoveDependencyAsync(id, dependsOnId, ct);

    [HttpPost("{id:int}/attachments"), RequestSizeLimit(210L * 1024 * 1024)]
    public async Task<TaskDetailDto> Attach(int id, IFormFile? file, [FromForm] string? kind, [FromForm] string? note, CancellationToken ct)
    { var (b, n, t) = await Http.ReadAsync(file, ct); return await svc.AttachAsync(id, n, t, b, kind, note, ct); }

    [HttpGet("{id:int}/attachments/{attachmentId:int}/file")]
    public async Task<IActionResult> Download(int id, int attachmentId, CancellationToken ct) { var (b, n, t) = await svc.DownloadAsync(id, attachmentId, ct); return File(b, t, n); }
}

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController(DocumentService svc) : ControllerBase
{
    /// <summary>box = inbox | outbox | returned | approval | signature | all.</summary>
    [HttpGet]
    public Task<List<DocumentDto>> List([FromQuery] string? box, [FromQuery] string? status, [FromQuery] string? q, [FromQuery] int? typeId, [FromQuery] string? holderId,
        [FromQuery] string? classification, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? taskId, CancellationToken ct)
        => svc.ListAsync(box, status, q, typeId, holderId, classification, from, to, taskId, ct);
    [HttpGet("{id:int}")] public Task<DocumentDetailDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost] public Task<DocumentDetailDto> Create([FromBody] DocumentUpsert r, CancellationToken ct) => svc.CreateAsync(r, ct);
    [HttpPut("{id:int}")] public Task<DocumentDetailDto> Update(int id, [FromBody] DocumentUpsert r, CancellationToken ct) => svc.UpdateAsync(id, r, ct);

    [HttpPost("{id:int}/versions"), RequestSizeLimit(210L * 1024 * 1024)]
    public async Task<DocumentDetailDto> Upload(int id, IFormFile? file, [FromForm] string? note, CancellationToken ct)
    { var (b, n, t) = await Http.ReadAsync(file, ct); return await svc.UploadVersionAsync(id, n, t, b, note, ct); }

    /// <summary>Preview (inline=true) or download a version. Both are written to the audit trail (FR-010).</summary>
    [HttpGet("{id:int}/versions/{versionId:int}/file")]
    public async Task<IActionResult> File(int id, int versionId, [FromQuery] bool inline, CancellationToken ct)
    { var (b, n, t) = await svc.DownloadVersionAsync(id, versionId, inline, ct); return inline ? Http.Inline(this, b, n, t) : File(b, t, n); }

    [HttpGet("{id:int}/signatures/{fieldId:int}/file")]
    public async Task<IActionResult> SignedFile(int id, int fieldId, CancellationToken ct) { var (b, n, t) = await svc.DownloadSignedAsync(id, fieldId, ct); return File(b, t, n); }

    [HttpPost("{id:int}/route/template")] public Task<DocumentDetailDto> Template(int id, [FromBody] TemplateChoice r, CancellationToken ct) => svc.ApplyTemplateAsync(id, r.TemplateId, ct);
    /// <summary>The initiator's proposed route (draft only). Validation issues come back on the document.</summary>
    [HttpPut("{id:int}/route")] public Task<DocumentDetailDto> Route(int id, [FromBody] RouteRequest r, CancellationToken ct) => svc.SetRouteAsync(id, r, ct);
    [HttpGet("{id:int}/route/validate")] public Task<List<RouteIssue>> Validate(int id, CancellationToken ct) => svc.ValidateAsync(id, ct);
    [HttpPut("{id:int}/signature-fields")] public Task<DocumentDetailDto> Fields(int id, [FromBody] SignatureFieldsRequest r, CancellationToken ct) => svc.SetSignatureFieldsAsync(id, r, ct);
    [HttpPost("{id:int}/submit")] public Task<DocumentDetailDto> Submit(int id, [FromBody] SubmitRequest r, CancellationToken ct) => svc.SubmitAsync(id, r, ct);
    /// <summary>Acknowledge, comment, recommend, concur, not-concur, approve, reject, return, dispatch, record. The version id must be the version the stage received.</summary>
    [HttpPost("{id:int}/stages/{stageId:int}/act")] public Task<DocumentDetailDto> Act(int id, int stageId, [FromBody] StageActionRequest r, CancellationToken ct) => svc.ActAsync(id, stageId, r, ct);
    /// <summary>Sign the approved version: password re-entry, explicit consent, authority check and integrity check (FR-019).</summary>
    [HttpPost("{id:int}/stages/{stageId:int}/sign")] public Task<DocumentDetailDto> Sign(int id, int stageId, [FromBody] SignRequest r, CancellationToken ct) => svc.SignAsync(id, stageId, r, ct);
    [HttpPost("{id:int}/inform")] public Task<DocumentDetailDto> Inform(int id, [FromBody] InformRequest r, CancellationToken ct) => svc.InformAsync(id, r, ct);
    [HttpPost("{id:int}/withdraw")] public Task<DocumentDetailDto> Withdraw(int id, [FromBody] ReasonRequest r, CancellationToken ct) => svc.WithdrawAsync(id, r.Reason, ct);

    [HttpPost("{id:int}/revise"), RequestSizeLimit(210L * 1024 * 1024)]
    public async Task<DocumentDetailDto> Revise(int id, IFormFile? file, [FromForm] string? reason, CancellationToken ct)
    { var (b, n, t) = await Http.ReadAsync(file, ct); return await svc.ReviseAsync(id, n, t, b, reason, ct); }

    [HttpPost("{id:int}/amendments")] public Task<DocumentDetailDto> Amend(int id, [FromBody] AmendmentRequest r, CancellationToken ct) => svc.RequestAmendmentAsync(id, r, ct);
    [HttpPost("{id:int}/amendments/{amendmentId:int}/decide")] public Task<DocumentDetailDto> Decide(int id, int amendmentId, [FromBody] AmendmentDecision r, CancellationToken ct) => svc.DecideAmendmentAsync(id, amendmentId, r, ct);
    [HttpGet("{id:int}/verify")] public Task<VerificationDto> Verify(int id, CancellationToken ct) => svc.VerifyAsync(id, ct);
    /// <summary>Verify by the code printed on a signature stamp.</summary>
    [HttpGet("verify/{code}")] public Task<VerificationDto> VerifyCode(string code, CancellationToken ct) => svc.VerifyCodeAsync(code, ct);
}

[ApiController]
[Route("api/workflow")]
[Authorize]
public class WorkflowController(WorkflowAdminService svc) : ControllerBase
{
    [HttpGet("types")] public Task<List<DocumentTypeDto>> Types(CancellationToken ct) => svc.TypesAsync(ct);
    [HttpPut("types")] public Task<DocumentTypeDto> SaveType([FromBody] DocumentTypeUpsert r, CancellationToken ct) => svc.SaveTypeAsync(r, ct);
    [HttpGet("templates")] public Task<List<TemplateDto>> Templates([FromQuery] int? typeId, [FromQuery] bool retired, CancellationToken ct) => svc.TemplatesAsync(typeId, retired, ct);
    /// <summary>A change to an active template is published as the next version; the previous version is retired.</summary>
    [HttpPut("templates")] public Task<TemplateDto> SaveTemplate([FromBody] TemplateUpsert r, CancellationToken ct) => svc.SaveTemplateAsync(r, ct);
    [HttpPost("templates/{id:int}/status/{status}")] public Task<TemplateDto> Status(int id, string status, CancellationToken ct) => svc.SetTemplateStatusAsync(id, status, ct);
    [HttpGet("authority")] public Task<List<AuthorityRuleDto>> Authority(CancellationToken ct) => svc.AuthorityAsync(ct);
    [HttpPut("authority")] public Task<AuthorityRuleDto> SaveAuthority([FromBody] AuthorityRuleUpsert r, CancellationToken ct) => svc.SaveAuthorityAsync(r, ct);
}

[ApiController]
[Route("api")]
[Authorize]
public class InsightsController(DashboardService dashboards, ReportService reports, NotificationService notifications, AuditService audit, UserContext users) : ControllerBase
{
    [HttpGet("dashboard")] public Task<DashboardDto> Dashboard(CancellationToken ct) => dashboards.GetAsync(ct);

    [HttpGet("reports")] public List<ReportInfo> Reports() => ReportService.Catalog;
    [HttpGet("reports/{key}")]
    public Task<ReportResult> Run(string key, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? userId, [FromQuery] int? unitId, [FromQuery] string? status, [FromQuery] int? typeId, [FromQuery] string? kind, CancellationToken ct)
        => reports.RunAsync(key, new ReportQuery(from, to, userId, unitId, status, typeId, kind), ct);
    /// <summary>format = csv | xlsx. The file carries the report date and the filters applied (FR-026).</summary>
    [HttpGet("reports/{key}/export")]
    public async Task<IActionResult> Export(string key, [FromQuery] string? format, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? userId, [FromQuery] int? unitId, [FromQuery] string? status, [FromQuery] int? typeId, [FromQuery] string? kind, CancellationToken ct)
    { var (b, n, t) = await reports.ExportAsync(key, new ReportQuery(from, to, userId, unitId, status, typeId, kind), format == "xlsx" ? "xlsx" : "csv", ct); return File(b, t, n); }

    [HttpGet("notifications")] public Task<List<NotificationDto>> Notifications([FromQuery] bool unread, CancellationToken ct) => notifications.ListAsync(unread, ct);
    [HttpGet("notifications/unread")] public async Task<object> Unread(CancellationToken ct) => new { count = await notifications.UnreadAsync(ct) };
    [HttpPost("notifications/read")] public async Task<IActionResult> Read([FromQuery] long? id, CancellationToken ct) { await notifications.MarkReadAsync(id, ct); return Ok(new { ok = true }); }

    /// <summary>The whole trail is for auditors; everybody else sees only their own actions.</summary>
    [HttpGet("audit")]
    public async Task<List<AuditDto>> Audit([FromQuery] string? kind, [FromQuery] string? action, [FromQuery] string? entityType, [FromQuery] string? entityId, [FromQuery] string? result, [FromQuery] string? q,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 500, CancellationToken ct = default)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var own = AccessPolicy.Can(me, Permissions.Audit) ? null : me.Id;
        return await audit.ListAsync(kind, action, entityType, entityId, result, q, from, to, limit, ct, own);
    }
}
