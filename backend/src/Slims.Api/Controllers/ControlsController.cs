using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Services;

namespace Slims.Api.Controllers;

public record CountAdjustRequest(string ReasonCode);
public record ReadRequest(long? Id);

/// <summary>Receiving area, put-away and bin moves (FR-INV-010/012).</summary>
[ApiController]
[Route("api/stock-control")]
[Authorize]
public class StockControlController(StoreControlService svc) : ControllerBase
{
    [HttpGet("receiving")] public Task<List<ItemDto>> Receiving([FromQuery] int? storeId, CancellationToken ct) => svc.ReceivingAsync(storeId, ct);
    /// <summary>Tells an item label from a bin label, so one scan box drives the put-away screen.</summary>
    [HttpGet("lookup")] public Task<ScanLookupDto> Lookup([FromQuery] string qr, CancellationToken ct) => svc.LookupAsync(qr, ct);
    /// <summary>Put away from Receiving, or move between bins: scan the items and the destination bin.</summary>
    [HttpPost("move")] public Task<MoveResultDto> Move([FromBody] MoveRequest r, CancellationToken ct) => svc.MoveAsync(r, ct);
}

[ApiController]
[Route("api/transfers")]
[Authorize]
public class TransfersController(StoreControlService svc) : ControllerBase
{
    [HttpGet] public Task<List<TransferDto>> List([FromQuery] string? status, CancellationToken ct) => svc.TransfersAsync(status, ct);
    [HttpGet("{id:int}")] public Task<TransferDto> Get(int id, CancellationToken ct) => svc.TransferAsync(id, ct);
    /// <summary>Despatch leg: scanned stock leaves the store and shows as in transit.</summary>
    [HttpPost] public Task<TransferDto> Despatch([FromBody] TransferDespatchRequest r, CancellationToken ct) => svc.DespatchAsync(r, ct);
    /// <summary>Receipt leg: scanned items land in the receiving store's Receiving area.</summary>
    [HttpPost("{id:int}/receive")] public Task<TransferDto> Receive(int id, [FromBody] TransferReceiveRequest r, CancellationToken ct) => svc.ReceiveTransferAsync(id, r, ct);
}

[ApiController]
[Route("api/adjustments")]
[Authorize]
public class AdjustmentsController(StoreControlService svc) : ControllerBase
{
    [HttpGet] public Task<List<AdjustmentDto>> List([FromQuery] string? status, CancellationToken ct) => svc.AdjustmentsAsync(status, ct);
    [HttpPost] public Task<AdjustmentDto> Raise([FromBody] AdjustmentRequest r, CancellationToken ct) => svc.RaiseAdjustmentAsync(r, ct);
    /// <summary>Second-person approval: only an approved adjustment changes the book.</summary>
    [HttpPost("{id:int}/decide")] public Task<AdjustmentDto> Decide(int id, [FromBody] DecisionRequest d, CancellationToken ct) => svc.DecideAdjustmentAsync(id, d, ct);
}

[ApiController]
[Route("api/counts")]
[Authorize]
public class CountsController(StoreControlService svc) : ControllerBase
{
    [HttpGet] public Task<List<CountDto>> List(CancellationToken ct) => svc.CountsAsync(ct);
    [HttpGet("{id:int}")] public Task<CountDto> Get(int id, CancellationToken ct) => svc.CountAsync(id, ct);
    [HttpPost] public Task<CountDto> Start([FromBody] CountStartRequest r, CancellationToken ct) => svc.StartCountAsync(r, ct);
    [HttpPost("{id:int}/scan")] public Task<CountScanDto> Scan(int id, [FromBody] CountScanRequest r, CancellationToken ct) => svc.CountScanAsync(id, r, ct);
    [HttpPost("{id:int}/submit")] public Task<CountDto> Submit(int id, CancellationToken ct) => svc.SubmitCountAsync(id, ct);
    [HttpPost("{id:int}/adjustments")] public Task<CountDto> Adjust(int id, [FromBody] CountAdjustRequest r, CancellationToken ct) => svc.RaiseCountAdjustmentsAsync(id, r.ReasonCode, ct);
    [HttpPost("{id:int}/close")] public Task<CountDto> Close(int id, CancellationToken ct) => svc.CloseCountAsync(id, ct);
}

[ApiController]
[Route("api/amendments")]
[Authorize]
public class AmendmentsController(AmendmentService svc) : ControllerBase
{
    [HttpGet] public Task<List<AmendmentDto>> List([FromQuery] string? status, [FromQuery] string? entityType, [FromQuery] string? entityRef, CancellationToken ct) => svc.ListAsync(status, entityType, entityRef, ct);
    [HttpGet("fields")] public AmendableFieldDto[] Fields() => AmendmentService.Fields;
    [HttpPost] public Task<AmendmentDto> Create([FromBody] AmendmentCreate r, CancellationToken ct) => svc.CreateAsync(r, ct);
    [HttpPost("{id:int}/decide")] public Task<AmendmentDto> Decide(int id, [FromBody] DecisionRequest d, CancellationToken ct) => svc.DecideAsync(id, d, ct);
}

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(InboxService svc) : ControllerBase
{
    [HttpGet] public Task<List<NotificationDto>> List([FromQuery] bool unread = false, [FromQuery] int take = 100, CancellationToken ct = default) => svc.ListAsync(unread, take, ct);
    [HttpGet("unread")] public async Task<object> Unread(CancellationToken ct) => new { count = await svc.UnreadAsync(ct) };
    [HttpPost("read")] public async Task<IActionResult> Read([FromBody] ReadRequest r, CancellationToken ct) { await svc.MarkReadAsync(r.Id, ct); return Ok(new { ok = true }); }
    [HttpGet("prefs")] public Task<NotifyPrefsDto> Prefs(CancellationToken ct) => svc.PrefsAsync(ct);
    /// <summary>Optional notices can be muted; assigned-work notices stay on whatever is sent (FR-NTF-005).</summary>
    [HttpPut("prefs")] public Task<NotifyPrefsDto> SavePrefs([FromBody] NotifyPrefs p, CancellationToken ct) => svc.SavePrefsAsync(p, ct);
}

[ApiController]
[Route("api/notify-settings")]
[Authorize]
public class NotifySettingsController(NotifySettingsService svc, UserContext users, AuditService audit, Application.Interfaces.IAppDbContext db, Application.Interfaces.IMessageSender sender) : ControllerBase
{
    [HttpGet] public async Task<NotifySettings> Get(CancellationToken ct) { await users.RequireAsync(Domain.Entities.Permissions.Configure, ct); return NotifySettingsService.Masked(await svc.GetAsync(ct)); }

    /// <summary>Secrets sent back as "********" (or empty) keep their stored value.</summary>
    [HttpPut]
    public async Task<NotifySettings> Save([FromBody] NotifySettings s, CancellationToken ct)
    {
        var me = await users.RequireAsync(Domain.Entities.Permissions.Configure, ct);
        var cur = await svc.GetAsync(ct);
        if (string.IsNullOrEmpty(s.SmtpPassword) || s.SmtpPassword == "********") s.SmtpPassword = cur.SmtpPassword;
        if (string.IsNullOrEmpty(s.SmsApiKey) || s.SmsApiKey == "********") s.SmsApiKey = cur.SmsApiKey;
        await svc.SaveAsync(s, ct);
        audit.Log("config", "notify-settings-updated", me, "config", "notify", next: new { s.EmailEnabled, s.SmtpHost, s.SmtpPort, s.FromAddress, s.SmsEnabled, s.PortalUrl });
        await db.SaveChangesAsync(ct);
        return NotifySettingsService.Masked(s);
    }

    [HttpPost("test")]
    public async Task<object> Test([FromQuery] string channel, [FromQuery] string to, CancellationToken ct)
    {
        await users.RequireAsync(Domain.Entities.Permissions.Configure, ct);
        var s = await svc.GetAsync(ct);
        try
        {
            if (channel == "sms") await sender.SendSmsAsync(s, to, "SLIMS test message", ct);
            else await sender.SendEmailAsync(s, [to], "[SLIMS] Test message", "E-mail delivery from SLIMS works.", null, ct);
            return new { ok = true, message = "Sent." };
        }
        catch (Exception e) { return new { ok = false, message = e.Message }; }
    }
}

[ApiController]
[Route("api/explore")]
[Authorize]
public class ExploreController(ExploreService svc) : ControllerBase
{
    /// <summary>FR-RPT-005: Summary → zone → site → requisition / issue → item → evidence.</summary>
    [HttpGet("drill")] public Task<DrillDto> Drill([FromQuery] string? level, [FromQuery] string? key, CancellationToken ct) => svc.DrillAsync(level, key, ct);
    /// <summary>FR-RPT-009: sites and installations for the map.</summary>
    [HttpGet("map")] public Task<MapDto> Map([FromQuery] string? zone, [FromQuery] int? siteId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) => svc.MapAsync(zone, siteId, from, to, ct);
}

[ApiController]
[Authorize]
public class SchedulesController(ScheduleService svc) : ControllerBase
{
    [HttpGet("api/report-schedules")] public Task<List<ScheduleDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost("api/report-schedules")] public Task<ScheduleDto> Create([FromBody] ScheduleUpsert r, CancellationToken ct) => svc.UpsertAsync(null, r, ct);
    [HttpPut("api/report-schedules/{id:int}")] public Task<ScheduleDto> Update(int id, [FromBody] ScheduleUpsert r, CancellationToken ct) => svc.UpsertAsync(id, r, ct);
    [HttpPost("api/report-schedules/{id:int}/run")] public Task<ReportRunDto> Run(int id, CancellationToken ct) => svc.RunNowAsync(id, ct);
    [HttpGet("api/report-runs/{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var f = await svc.OpenRunAsync(id, ct);
        return f is null ? NotFound() : File(f.Value.Item1, "text/csv", f.Value.Item2);
    }
}

[ApiController]
[Route("api/automation")]
[Authorize]
public class AutomationController(AutomationService svc, UserContext users) : ControllerBase
{
    /// <summary>Runs the time-driven rules now instead of waiting for the background worker.</summary>
    [HttpPost("run")] public async Task<AutomationResult> Run(CancellationToken ct) { await users.RequireAsync(Domain.Entities.Permissions.Configure, ct); return await svc.RunAsync(ct); }
}

/// <summary>Mobile setup and the SLIMS Field app (FR-SEC-009, FR-FLD-007/012/013).</summary>
[ApiController]
[Route("api/mobile")]
public class MobileController(DeviceService devices, MobileService mobile, InstallationService installs, IssueService issues, ReturnService returns, ConfigService configs, IConfiguration config) : ControllerBase
{
    // ---- portal side --------------------------------------------------------------------------------------
    /// <summary>A one-time setup QR for the signed-in user (or, for the administrator, for another user).</summary>
    [HttpPost("enrolment"), Authorize] public Task<EnrolmentDto> Enrolment([FromBody] EnrolmentRequest r, CancellationToken ct) => devices.CreateEnrolmentAsync(r, ct);
    [HttpGet("mine"), Authorize] public Task<MyMobileDto> Mine(CancellationToken ct) => devices.MineAsync(ct);
    [HttpGet("devices"), Authorize] public Task<List<DeviceDto>> Devices(CancellationToken ct) => devices.AllAsync(ct);
    /// <summary>Re-authorisation of a replacement phone; the phone in use until now is retired.</summary>
    [HttpPost("devices/{id:guid}/approve"), Authorize] public Task<DeviceDto> Approve(Guid id, CancellationToken ct) => devices.ApproveAsync(id, ct);
    [HttpPost("devices/{id:guid}/revoke"), Authorize] public Task<DeviceDto> Revoke(Guid id, [FromBody] ReasonBody? r, CancellationToken ct) => devices.RevokeAsync(id, r?.Reason, ct);

    string ApkPath => Path.Combine(config["App:DownloadsDir"] ?? Path.Combine(AppContext.BaseDirectory, "downloads"), "slims-field.apk");

    /// <summary>The Android app itself, for installing from the phone's browser.</summary>
    [HttpGet("app.apk"), AllowAnonymous]
    public IActionResult Apk() => System.IO.File.Exists(ApkPath) ? PhysicalFile(ApkPath, "application/vnd.android.package-archive", "SLIMS-Field.apk") : NotFound(new { error = "The field app has not been published on this server yet." });

    [HttpGet("app-info"), AllowAnonymous]
    public async Task<object> AppInfo(CancellationToken ct)
    {
        var f = new FileInfo(ApkPath);
        var meta = Path.ChangeExtension(ApkPath, ".json");
        var cfg = await configs.GetAsync(ct);
        return new
        {
            available = f.Exists, size = f.Exists ? f.Length : 0, updated = f.Exists ? f.LastWriteTimeUtc : (DateTime?)null,
            version = System.IO.File.Exists(meta) ? System.IO.File.ReadAllText(meta).Trim() : null, url = "/api/mobile/app.apk",
            phoneServerUrl = string.IsNullOrWhiteSpace(cfg.Field.PhoneServerUrl) ? null : cfg.Field.PhoneServerUrl.Trim().TrimEnd('/'),
            lanAddresses = LanAddresses(),
        };
    }

    /// <summary>This PC's IPv4 addresses on the network, the one with a default gateway first — used when the portal is opened as localhost,
    /// because a phone cannot reach "localhost".</summary>
    static List<string> LanAddresses()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                            && n.NetworkInterfaceType is not (System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel))
                .Select(n => (p: n.GetIPProperties(), virt: n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || n.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.p.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
                .ThenBy(x => x.virt)
                .SelectMany(x => x.p.UnicastAddresses.Select(a => a.Address))
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."))
                .Select(a => a.ToString()).Distinct().ToList();
        }
        catch { return []; }
    }

    // ---- app side ------------------------------------------------------------------------------------------
    [HttpPost("enrol"), AllowAnonymous] public Task<EnrolResultDto> Enrol([FromBody] EnrolRequest r, CancellationToken ct) => devices.EnrolAsync(r, ct);
    [HttpPost("session"), AllowAnonymous] public Task<DeviceSessionDto> Session([FromBody] DeviceSessionRequest r, CancellationToken ct) => devices.SessionAsync(r, ct);
    [HttpPost("setup-check"), Authorize, RequestSizeLimit(20L * 1024 * 1024)] public Task<SetupCheckDto> SetupCheck([FromBody] SetupCheckRequest r, CancellationToken ct) => devices.SetupCheckAsync(r, ct);
    [HttpGet("bootstrap"), Authorize] public Task<MobileBootstrapDto> Bootstrap(CancellationToken ct) => mobile.BootstrapAsync(ct);
    /// <summary>Installation from the queue: BEFORE and AFTER photographs (base64 + SHA-256) with the fix of each, and a client transaction id.</summary>
    [HttpPost("installations"), Authorize, RequestSizeLimit(40L * 1024 * 1024)] public Task<InstallationDto> Install([FromBody] MobileInstallationRequest r, CancellationToken ct) => installs.SubmitMobileAsync(r, ct);
    [HttpPost("acknowledge"), Authorize] public Task<IssueDto> Acknowledge([FromBody] MobileAckRequest r, CancellationToken ct) => issues.AcknowledgeAsync(r.IssueId, new AckRequest(r.Lat, r.Lng, r.AccuracyM), ct);
    [HttpPost("returns"), Authorize] public Task<ReturnDto> Return([FromBody] ReturnReportRequest r, CancellationToken ct) => returns.ReportAsync(r, ct);
    [HttpPost("findings/{id:int}"), Authorize, RequestSizeLimit(40L * 1024 * 1024)] public Task<InstallationDto> Findings(int id, [FromBody] FindingsRequest r, CancellationToken ct) => installs.SubmitFindingsAsync(id, r, ct);
}
