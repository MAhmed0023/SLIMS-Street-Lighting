using Microsoft.EntityFrameworkCore;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;

namespace Slims.Application.Services;

/// <summary>Entity → DTO mapping and the standard eager-loading queries the services share.</summary>
public static class Map
{
    public static IQueryable<Item> Items(IAppDbContext db) => db.Items
        .Include(i => i.Article)!.ThenInclude(a => a!.PurchaseOrder)!.ThenInclude(p => p!.Vendor)
        .Include(i => i.StoreLocation)!.ThenInclude(l => l!.Store)
        .Include(i => i.Site);
    public static IQueryable<Installation> Installations(IAppDbContext db) => db.Installations
        .Include(x => x.Item)!.ThenInclude(i => i!.Article).Include(x => x.Site).Include(x => x.Before).Include(x => x.After);
    public static IQueryable<ReturnNote> Returns(IAppDbContext db) => db.ReturnNotes
        .Include(x => x.Item)!.ThenInclude(i => i!.Article)!.ThenInclude(a => a!.PurchaseOrder)!.ThenInclude(p => p!.Vendor)
        .Include(x => x.Item)!.ThenInclude(i => i!.StoreLocation)
        .Include(x => x.Site).Include(x => x.Store);
    public static IQueryable<Requisition> Requisitions(IAppDbContext db) => db.Requisitions
        .Include(r => r.Site).Include(r => r.Store).Include(r => r.Lines).ThenInclude(l => l.Article);
    public static IQueryable<Issue> Issues(IAppDbContext db) => db.Issues.Include(x => x.Requisition).Include(x => x.Site).Include(x => x.Store);

    /// <summary>Stored times are UTC; marking them so makes the JSON carry the "Z" and every client convert correctly.</summary>
    public static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    public static DateTime? Utc(DateTime? d) => d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);

    public static DateOnly? WarrantyExpires(Item i) => i.InstalledAt is DateTime d && i.Article is not null ? DateOnly.FromDateTime(d).AddMonths(i.Article.WarrantyMonths) : null;

    public static ItemDto Item(Item i)
    {
        var a = i.Article!;
        return new(i.Id, i.QrCode, i.ArticleId, a.ItemCode, a.Description, a.Category, i.Serial, i.Qty, a.Uom, i.Status.ToString(),
            i.StoreLocation?.StoreId, i.StoreLocation?.Store?.Code, i.StoreLocationId, i.StoreLocation?.FullCode, i.SiteId, i.Site?.Code, i.Site?.Name,
            a.PurchaseOrder?.Number ?? "", a.PurchaseOrder?.Vendor?.Name ?? "", a.Rate, a.WarrantyMonths, WarrantyExpires(i),
            i.GrnId, i.RequisitionId, i.IssueId, i.InstallationId, i.ReturnNoteId, i.ReceivedAt, i.IssuedAt, i.InstalledAt, i.ReturnedAt, i.LabelPrints, i.UpdatedAt, i.UpdatedBy);
    }

    public static EvidenceDto Evidence(Evidence e) => new(e.Id, e.Kind, e.Name, e.ContentType, e.Size, e.Lat, e.Lng, e.AccuracyM, DateTime.SpecifyKind(e.CapturedAt, DateTimeKind.Utc), e.UploadedBy, DateTime.SpecifyKind(e.UploadedAt, DateTimeKind.Utc), $"/api/files/{e.Id}",
        e.Sha256, e.StampedPath is null ? null : $"/api/files/{e.Id}?variant=stamped", e.DeviceId);

    public static InstallationDto Installation(Installation x)
    {
        var s = x.Site!; var it = x.Item!; var a = it.Article!;
        return new(x.Id, x.Number, x.ItemId, it.QrCode, a.ItemCode, a.Description, x.SiteId, s.Code, s.Name, s.Zone, s.Lat, s.Lng, s.GeofenceM,
            x.Officer, x.OfficerId, x.PoleId, x.Lat, x.Lng, x.AccuracyM, Math.Round(x.DistanceM, 1), x.GeoStatus.ToString(), x.Remarks, Utc(x.CapturedAt), Utc(x.SubmittedAt), x.Offline,
            x.VerifiedBy, Utc(x.VerifiedAt), x.Justification, x.ReplacesItemId, Evidence(x.Before!), Evidence(x.After!),
            x.ClientTxnId, x.DeviceId, Utc(x.QueuedAt), (int)(DateTime.UtcNow - x.SubmittedAt).TotalDays, x.EscalationLevel, Utc(x.EscalatedAt),
            x.FvRequestedBy, Utc(x.FvRequestedAt), x.FvRequestNote, x.FvAssignedToId, x.FvFindings, x.FvFindingsBy, Utc(x.FvFindingsAt),
            (x.FvEvidenceIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => $"/api/files/{id}?variant=stamped").ToList());
    }

    public static ReturnDto Return(ReturnNote r)
    {
        var it = r.Item!; var a = it.Article!;
        int? service = it.InstalledAt is DateTime d ? (int)((r.ReportedAt - d).TotalDays) : null;
        return new(r.Id, r.Number, r.ItemId, it.QrCode, a.ItemCode, a.Description, a.PurchaseOrder?.Number ?? "", a.PurchaseOrder?.Vendor?.Name ?? "", r.SiteId, r.Site?.Code, r.Site?.Name,
            r.StoreId, r.Store?.Code, it.StoreLocation?.FullCode, r.Reason, r.Kind, r.ReportedBy, r.ReportedAt, r.ReceivedBy, r.ReceivedAt,
            r.Classification.ToString(), r.WarrantyEligible, r.WarrantyExpires, r.ClaimRef, r.ClassifiedAt, r.DisposalApprovedAt, r.ScrapValue, r.ClosedAt,
            r.Remarks, it.InstalledAt, service, it.Status.ToString());
    }

    public static RequisitionDto Requisition(Requisition r, Dictionary<int, decimal>? available, List<int>? issueIds)
    {
        var lines = r.Lines.OrderBy(l => l.Id).Select(l => new ReqLineDto(l.Id, l.ArticleId, l.Article!.ItemCode, l.Article.Description, l.Article.Uom, l.QtyRequested, l.QtyApproved, l.QtyIssued, l.Shortfall,
            available is not null && available.TryGetValue(l.ArticleId, out var av) ? av : 0, l.IndentRaised)).ToList();
        var value = r.Lines.Sum(l => (l.QtyApproved > 0 ? l.QtyApproved : l.QtyRequested) * (l.Article?.Rate ?? 0));
        return new(r.Id, r.Number, r.SiteId, r.Site!.Code, r.Site.Name, r.Site.Zone, r.StoreId, r.Store!.Code, r.RaisedBy, r.RaisedById, r.RaisedAt, r.NeedBy,
            r.Status.ToString(), r.Purpose, r.ApprovedBy, r.DecidedAt, r.DecisionNote, r.IssueSlipNo, r.Value > 0 ? r.Value : value, lines, issueIds ?? new(),
            r.RequiredLevel, r.CurrentLevel, r.RecommendedBy, r.SubmittedAt, r.LapsedAt);
    }

    public static IssueDto Issue(Issue x, List<Item> items) => new(x.Id, x.Number, x.GatePassNo, x.RequisitionId, x.Requisition?.Number ?? "", x.StoreId, x.Store?.Code ?? "", x.SiteId, x.Site?.Code ?? "", x.Site?.Name ?? "",
        x.IssuedBy, x.IssuedAt, x.IssuedTo, x.Vehicle, x.ReceivedById, x.ReceivedAt, x.ItemCount, x.Qty, items.Sum(i => i.Qty * (i.Article?.Rate ?? 0)), x.Remarks, items.Select(Item).ToList());

    public static GrnDto Grn(Grn g) => new(g.Id, g.Number, g.PurchaseOrderId, g.PurchaseOrder?.Number ?? "", g.PurchaseOrder?.Vendor?.Name ?? "", g.StoreId, g.Store?.Code ?? "", g.ChallanNo, g.InvoiceNo, g.InvoiceDate,
        g.PostedBy, g.PostedAt, g.ItemCount, g.Qty, g.Value, g.Remarks, g.Reversed, g.Condition, g.RejectedCount);
    public static DiscrepancyDto Discrepancy(DiscrepancyNote d) => new(d.Id, d.Number, d.PurchaseOrderId, d.PurchaseOrder?.Number ?? "", d.PurchaseOrder?.Vendor?.Name ?? "", d.ArticleId, d.Article?.ItemCode, d.Kind, d.Qty, d.Reason, d.Status, d.RaisedAt, d.Resolution, d.ResolvedAt);

    public static LocationDto Location(StoreLocation l, int items) => new(l.Id, l.StoreId, l.Store?.Code ?? "", l.Section, l.Rack, l.Bin, l.FullCode, l.Kind, l.Active, items);
    public static VendorDto Vendor(Vendor v, int pos) => new(v.Id, v.Code, v.Name, v.Contact, v.Phone, v.Email, v.Active, pos);
    public static ArticleDto Article(Article a, decimal poQty, decimal received, decimal onHand, decimal reserved, decimal installed, decimal quarantine)
        => new(a.Id, a.ItemCode, a.Category, a.SubCategory, a.Spec, a.Description, a.Uom, a.Serialised, a.PackSize, a.PurchaseOrderId, a.PurchaseOrder?.Number ?? "", a.VendorId, a.Vendor?.Name ?? "",
            a.Rate, a.WarrantyMonths, a.ExpectedLifeMonths, a.ReorderLevel, a.Status, a.ItemsGenerated, poQty, received, onHand, reserved, installed, quarantine, a.CreatedAt);
}
