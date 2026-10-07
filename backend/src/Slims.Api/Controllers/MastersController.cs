using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Application.Services;
using Slims.Domain.Rules;

namespace Slims.Api.Controllers;

[ApiController]
[Route("api/stores")]
[Authorize]
public class StoresController(MasterService svc) : ControllerBase
{
    [HttpGet] public Task<List<StoreDto>> List(CancellationToken ct) => svc.StoresAsync(ct);
    [HttpPost] public async Task<object> Create([FromBody] StoreUpsert r, CancellationToken ct) => new { id = await svc.UpsertStoreAsync(null, r, ct) };
    [HttpPut("{id:int}")] public async Task<object> Update(int id, [FromBody] StoreUpsert r, CancellationToken ct) => new { id = await svc.UpsertStoreAsync(id, r, ct) };
    [HttpPost("{storeId:int}/locations")] public async Task<object> AddLocation(int storeId, [FromBody] LocationUpsert r, CancellationToken ct) => new { id = await svc.UpsertLocationAsync(storeId, null, r, ct) };
    [HttpPut("{storeId:int}/locations/{id:int}")] public async Task<object> UpdateLocation(int storeId, int id, [FromBody] LocationUpsert r, CancellationToken ct) => new { id = await svc.UpsertLocationAsync(storeId, id, r, ct) };
    /// <summary>Bin labels: the QR payload for every active location of the store.</summary>
    [HttpGet("{storeId:int}/labels")] public Task<List<object>> Labels(int storeId, CancellationToken ct) => svc.LocationLabelsAsync(storeId, ct);
}

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController(MasterService svc) : ControllerBase
{
    [HttpGet] public Task<List<SiteDto>> List([FromQuery] string? zone, [FromQuery] string? q, CancellationToken ct) => svc.SitesAsync(zone, q, ct);
    [HttpPost] public async Task<object> Create([FromBody] SiteUpsert r, CancellationToken ct) => new { id = await svc.UpsertSiteAsync(null, r, ct) };
    [HttpPut("{id:int}")] public async Task<object> Update(int id, [FromBody] SiteUpsert r, CancellationToken ct) => new { id = await svc.UpsertSiteAsync(id, r, ct) };
    /// <summary>FR-MD-04: bulk upload from a survey file (JSON array of site rows). Each row is validated; errors are reported by row.</summary>
    [HttpPost("bulk")] public Task<BulkResultDto> Bulk([FromBody] List<SiteUpsert> rows, CancellationToken ct) => svc.BulkSitesAsync(rows, ct);
}

[ApiController]
[Route("api/vendors")]
[Authorize]
public class VendorsController(MasterService svc) : ControllerBase
{
    [HttpGet] public Task<List<VendorDto>> List(CancellationToken ct) => svc.VendorsAsync(ct);
    [HttpPost] public async Task<object> Create([FromBody] VendorUpsert r, CancellationToken ct) => new { id = await svc.UpsertVendorAsync(null, r, ct) };
    [HttpPut("{id:int}")] public async Task<object> Update(int id, [FromBody] VendorUpsert r, CancellationToken ct) => new { id = await svc.UpsertVendorAsync(id, r, ct) };
}

[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrdersController(MasterService svc) : ControllerBase
{
    [HttpGet] public Task<List<PoDto>> List([FromQuery] string? status, CancellationToken ct) => svc.PurchaseOrdersAsync(status, ct);
    [HttpPost] public async Task<object> Create([FromBody] PoUpsertRequest r, CancellationToken ct) => new { id = await svc.UpsertPoAsync(null, r, ct) };
    [HttpPut("{id:int}")] public async Task<object> Update(int id, [FromBody] PoUpsertRequest r, CancellationToken ct) => new { id = await svc.UpsertPoAsync(id, r, ct) };
    [HttpPost("{id:int}/toggle")] public async Task<IActionResult> Toggle(int id, CancellationToken ct) { await svc.ClosePoAsync(id, ct); return Ok(new { ok = true }); }
}

public record ArticleUpdateRequest(decimal ReorderLevel, string Status, int? ExpectedLifeMonths);

[ApiController]
[Route("api/articles")]
[Authorize]
public class ArticlesController(MasterService svc) : ControllerBase
{
    [HttpGet] public Task<List<ArticleDto>> List([FromQuery] string? q, [FromQuery] string? status, CancellationToken ct) => svc.ArticlesAsync(q, status, ct);
    /// <summary>Create the article for a PO line, item-code wise, and generate its unique QR series (one label per piece, or per pack for bulk).</summary>
    [HttpPost] public Task<ArticleDto> Create([FromBody] ArticleCreateRequest r, CancellationToken ct) => svc.CreateArticleAsync(r, ct);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] ArticleUpdateRequest r, CancellationToken ct) { await svc.UpdateArticleAsync(id, r.ReorderLevel, r.Status, r.ExpectedLifeMonths, ct); return Ok(new { ok = true }); }
    [HttpGet("{id:int}/items")] public Task<List<ItemDto>> Items(int id, [FromQuery] string? status, CancellationToken ct) => svc.ArticleItemsAsync(id, status, ct);
    /// <summary>Marks the labels as printed (re-prints are logged) and returns the rows for the label sheet.</summary>
    [HttpPost("{id:int}/labels")] public Task<List<ItemDto>> Labels(int id, [FromQuery] int? from, [FromQuery] int? to, CancellationToken ct) => svc.LabelsAsync(id, from, to, ct);
}

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(MasterService svc, IQrRenderer qr) : ControllerBase
{
    /// <summary>Full detail for an item by its id or its QR payload: current state, history, installation and return.</summary>
    [HttpGet("{idOrQr}")] public Task<ItemDetailDto> Get(string idOrQr, CancellationToken ct) => svc.ItemAsync(Uri.UnescapeDataString(idOrQr), ct);
    /// <summary>The QR symbol as PNG. Only ever encodes a valid SLIMS payload.</summary>
    [HttpGet("qr.png"), AllowAnonymous]
    public IActionResult Qr([FromQuery] string payload, [FromQuery] int size = 6)
    {
        var appLink = Uri.TryCreate(payload, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && u.AbsolutePath == "/api/mobile/app.apk";
        if (!Codes.TryParse(payload, out _, out _) && Codes.ParseLocation(payload) is null && !payload.StartsWith("{\"t\":\"slims-setup\"") && !appLink) return BadRequest(new { error = "Invalid SLIMS payload." });
        return File(qr.Png(payload, Math.Clamp(size, 2, 20)), "image/png");
    }
}
