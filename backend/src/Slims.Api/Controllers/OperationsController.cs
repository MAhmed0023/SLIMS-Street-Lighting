using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Services;

namespace Slims.Api.Controllers;

public record ReasonRequest(string Reason);
public record ScanBatchRequest(string Qr, int RequisitionId, string[]? Already);
public record ReinwardRequest(int StoreLocationId, string? Remarks);

[ApiController]
[Route("api/grn")]
[Authorize]
public class GrnController(GrnService svc) : ControllerBase
{
    [HttpGet] public Task<List<GrnDto>> List([FromQuery] int? storeId, [FromQuery] int? poId, CancellationToken ct) => svc.ListAsync(storeId, poId, ct);
    [HttpGet("{id:int}/items")] public Task<List<ItemDto>> Items(int id, CancellationToken ct) => svc.GrnItemsAsync(id, ct);
    /// <summary>Validate one scanned label against the PO being received (FR-GR-03). Nothing is posted.</summary>
    [HttpPost("scan")] public Task<ScanResultDto> Scan([FromBody] GrnScanRequest r, CancellationToken ct) => svc.ScanAsync(r, ct);
    /// <summary>Post the GRN: every scanned label becomes Received at the chosen bin location (FR-GR-07/08).</summary>
    [HttpPost] public Task<GrnDto> Post([FromBody] GrnPostRequest r, CancellationToken ct) => svc.PostAsync(r, ct);
    [HttpPost("{id:int}/reverse")] public async Task<IActionResult> Reverse(int id, [FromBody] ReasonRequest r, CancellationToken ct) { await svc.ReverseAsync(id, r.Reason, ct); return Ok(new { ok = true }); }
    [HttpGet("discrepancies")] public Task<List<DiscrepancyDto>> Discrepancies([FromQuery] string? status, CancellationToken ct) => svc.DiscrepanciesAsync(status, ct);
    [HttpPost("discrepancies")] public Task<DiscrepancyDto> Raise([FromBody] DiscrepancyRequest r, CancellationToken ct) => svc.RaiseDiscrepancyAsync(r, ct);
    [HttpPost("discrepancies/{id:int}/resolve")] public async Task<IActionResult> Resolve(int id, [FromBody] ResolveRequest r, CancellationToken ct) { await svc.ResolveDiscrepancyAsync(id, r.Resolution, ct); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController(RequisitionService svc) : ControllerBase
{
    [HttpGet] public Task<List<RequisitionDto>> List([FromQuery] string? status, [FromQuery] int? storeId, [FromQuery] int? siteId, CancellationToken ct) => svc.ListAsync(status, storeId, siteId, ct);
    [HttpGet("{id:int}")] public Task<RequisitionDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpGet("availability/{storeId:int}")] public Task<Dictionary<int, decimal>> Availability(int storeId, CancellationToken ct) => svc.AvailabilityAsync(storeId, ct);
    [HttpPost] public Task<RequisitionDto> Create([FromBody] RequisitionUpsert r, CancellationToken ct) => svc.CreateAsync(r, ct);
    [HttpPost("{id:int}/submit")] public Task<RequisitionDto> Submit(int id, CancellationToken ct) => svc.SubmitAsync(id, ct);
    /// <summary>Approve (in full or part — stock is reserved and an Issue Slip generated) or reject with a reason.</summary>
    [HttpPost("{id:int}/decide")] public Task<RequisitionDto> Decide(int id, [FromBody] RequisitionDecision d, CancellationToken ct) => svc.DecideAsync(id, d, ct);
    [HttpPost("{id:int}/cancel")] public Task<RequisitionDto> Cancel(int id, [FromBody] ReasonRequest? r, CancellationToken ct) => svc.CancelAsync(id, r?.Reason, ct);
}

[ApiController]
[Route("api/issues")]
[Authorize]
public class IssuesController(IssueService svc) : ControllerBase
{
    [HttpGet] public Task<List<IssueDto>> List([FromQuery] int? storeId, [FromQuery] int? siteId, [FromQuery] bool? unacknowledged, CancellationToken ct) => svc.ListAsync(storeId, siteId, unacknowledged, ct);
    [HttpGet("{id:int}")] public Task<IssueDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpGet("available")] public Task<List<IssueAvailableDto>> Available([FromQuery] int requisitionId, CancellationToken ct) => svc.AvailableAsync(requisitionId, ct);
    /// <summary>Validate one scanned label against the approved requisition: right item, within approved quantity, not already issued (FR-IS-03).</summary>
    [HttpPost("scan")] public Task<ScanResultDto> Scan([FromBody] ScanBatchRequest r, CancellationToken ct) => svc.ScanAsync(new IssueScanRequest(r.Qr, r.RequisitionId), r.Already, ct);
    /// <summary>Post the issue: custody of every scanned item moves from the store bin to the site; issue note and gate pass are numbered.</summary>
    [HttpPost] public Task<IssueDto> Post([FromBody] IssuePostRequest r, CancellationToken ct) => svc.PostAsync(r, ct);
    [HttpPost("{id:int}/acknowledge")] public Task<IssueDto> Acknowledge(int id, [FromBody] AckRequest a, CancellationToken ct) => svc.AcknowledgeAsync(id, a, ct);
    /// <summary>The officer's work list: material in the field for the sites this user can see.</summary>
    [HttpGet("field")] public Task<List<ItemDto>> Field([FromQuery] int? siteId, CancellationToken ct) => svc.FieldItemsAsync(siteId, ct);
}

/// <summary>Multipart form from the field app: two live-captured photographs plus the coordinates recorded at capture.</summary>
public class InstallationForm
{
    public string Qr { get; set; } = "";
    public string? PoleId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double AccuracyM { get; set; }
    public DateTime CapturedAt { get; set; }
    public string? Remarks { get; set; }
    public bool Offline { get; set; }
    public string? ReplacesQr { get; set; }
    public IFormFile? Before { get; set; }
    public double? BeforeLat { get; set; }
    public double? BeforeLng { get; set; }
    public double? BeforeAccuracyM { get; set; }
    public DateTime? BeforeAt { get; set; }
    public IFormFile? After { get; set; }
    public double? AfterLat { get; set; }
    public double? AfterLng { get; set; }
    public double? AfterAccuracyM { get; set; }
    public DateTime? AfterAt { get; set; }
}

[ApiController]
[Route("api/installations")]
[Authorize]
public class InstallationsController(InstallationService svc) : ControllerBase
{
    [HttpGet] public Task<List<InstallationDto>> List([FromQuery] int? siteId, [FromQuery] string? geoStatus, [FromQuery] string? officerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? zone, CancellationToken ct) => svc.ListAsync(siteId, geoStatus, officerId, from, to, zone, ct);
    [HttpGet("{id:int}")] public Task<InstallationDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);

    /// <summary>Submit an installation: BEFORE and AFTER photographs with their coordinates and times, plus the item QR scanned at the pole. The server measures the distance to the site and confirms or raises an exception.</summary>
    [HttpPost, RequestSizeLimit(40L * 1024 * 1024)]
    public async Task<InstallationDto> Submit([FromForm] InstallationForm f, CancellationToken ct)
    {
        if (f.Before is null || f.After is null) throw AppException.Rule("BR-013", "Both BEFORE and AFTER photographs are required.");
        EvidenceUpload Ev(string kind, IFormFile file, double? lat, double? lng, double? acc, DateTime? at) => new(kind, file.FileName, file.ContentType, file.Length, file.OpenReadStream(), lat, lng, acc, at ?? f.CapturedAt);
        var s = new InstallationSubmit(f.Qr, f.PoleId, f.Lat, f.Lng, f.AccuracyM, f.CapturedAt, f.Remarks, f.Offline, f.ReplacesQr,
            Ev("before", f.Before, f.BeforeLat, f.BeforeLng, f.BeforeAccuracyM, f.BeforeAt), Ev("after", f.After, f.AfterLat, f.AfterLng, f.AfterAccuracyM, f.AfterAt));
        return await svc.SubmitAsync(s, ct);
    }

    /// <summary>Supervisor decision on a geo-fence exception: approve with justification, or require re-capture.</summary>
    [HttpPost("{id:int}/verify")] public Task<InstallationDto> Verify(int id, [FromBody] VerifyRequest r, CancellationToken ct) => svc.VerifyAsync(id, r, ct);
    /// <summary>FR-EXC-007: ask the Lighting Supervisor to verify on the ground.</summary>
    [HttpPost("{id:int}/field-verification")] public Task<InstallationDto> RequestFieldVerification(int id, [FromBody] FieldVerifyRequest r, CancellationToken ct) => svc.RequestFieldVerificationAsync(id, r, ct);
    /// <summary>The supervisor's written findings from the portal (photographs come from the field app).</summary>
    [HttpPost("{id:int}/findings")] public Task<InstallationDto> Findings(int id, [FromBody] FindingsRequest r, CancellationToken ct) => svc.SubmitFindingsAsync(id, r with { Photos = null }, ct);
}

[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController(InstallationService svc) : ControllerBase
{
    /// <summary>The original photograph, or with ?variant=stamped the server-generated stamped copy.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromQuery] string? variant, CancellationToken ct)
    {
        var f = await svc.OpenEvidenceAsync(id, variant == "stamped", ct);
        if (f is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(f.Value.stream, f.Value.contentType, f.Value.name);
    }
}

[ApiController]
[Route("api/returns")]
[Authorize]
public class ReturnsController(ReturnService svc) : ControllerBase
{
    [HttpGet] public Task<List<ReturnDto>> List([FromQuery] string? stage, [FromQuery] int? storeId, CancellationToken ct) => svc.ListAsync(stage, storeId, ct);
    [HttpGet("overdue")] public Task<List<ReturnDto>> Overdue(CancellationToken ct) => svc.OverdueAsync(ct);
    [HttpGet("{id:int}")] public Task<ReturnDto> Get(int id, CancellationToken ct) => svc.GetAsync(id, ct);
    /// <summary>Officer reports an item out of order / dismantled / unused at site.</summary>
    [HttpPost("report")] public Task<ReturnDto> Report([FromBody] ReturnReportRequest r, CancellationToken ct) => svc.ReportAsync(r, ct);
    /// <summary>Store scans the item back in — quarantine for failed items, stock for unused.</summary>
    [HttpPost("receive")] public Task<ReturnDto> Receive([FromBody] ReturnReceiveRequest r, CancellationToken ct) => svc.ReceiveAsync(r, ct);
    [HttpPost("{id:int}/classify")] public Task<ReturnDto> Classify(int id, [FromBody] ReturnClassifyRequest r, CancellationToken ct) => svc.ClassifyAsync(id, r, ct);
    [HttpPost("{id:int}/dispose")] public Task<ReturnDto> Dispose(int id, [FromBody] ReturnDisposeRequest r, CancellationToken ct) => svc.DisposeAsync(id, r, ct);
    [HttpPost("{id:int}/reinward")] public Task<ReturnDto> Reinward(int id, [FromBody] ReinwardRequest r, CancellationToken ct) => svc.ReinwardAsync(id, r.StoreLocationId, r.Remarks, ct);
}

[ApiController]
[Route("api/stock")]
[Authorize]
public class StockController(StockService svc) : ControllerBase
{
    [HttpGet] public Task<StockSummaryDto> Position([FromQuery] int? storeId, [FromQuery] string? q, CancellationToken ct) => svc.PositionAsync(storeId, q, ct);
    [HttpGet("location/{id:int}")] public Task<List<ItemDto>> Location(int id, CancellationToken ct) => svc.LocationItemsAsync(id, ct);
}

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(StockService svc) : ControllerBase
{
    [HttpGet] public Task<DashboardDto> Get(CancellationToken ct) => svc.DashboardAsync(ct);
}

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(ReportService svc) : ControllerBase
{
    [HttpGet] public object Catalogue() => ReportService.Catalogue.Select(c => new { key = c.Key, title = c.Title, audience = c.Audience });
    [HttpGet("{key}")] public Task<ReportDto> Run(string key, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? zone, [FromQuery] int? storeId, [FromQuery] int? siteId, [FromQuery] int? poId, CancellationToken ct)
        => svc.RunAsync(key, new ReportFilters(from, to, zone, storeId, siteId, poId), ct);
}
