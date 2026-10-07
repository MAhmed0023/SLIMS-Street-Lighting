using Slims.Domain.Config;
using Slims.Domain.Entities;

namespace Slims.Application.Dtos;

// ---- auth & users ------------------------------------------------------------------------
public record LoginRequest(string Id, string? Password);
public record ChangePasswordRequest(string? Old, string New);
public record SetPasswordRequest(string Password);
public record TokenResponse(string AccessToken, DateTime ExpiresAt, UserDto User, bool MustChangePassword);
public record UserDto(string Id, string Name, string? Email, string? EmployeeId, string? Designation, string? Phone, string RoleKey, string RoleLabel, string? Zone,
    int[] StoreIds, int[] SiteIds, bool Active, bool HasPassword, bool MustChangePassword, DateTime? LastLogin, DateTime? LockedUntil,
    Dictionary<string, bool> Permissions, string ScopeDescription);
public record UserPublicDto(string Id, string Name, string RoleKey, string RoleLabel, string? Designation, bool HasPassword, bool Active);
public record UserUpsertRequest(string Id, string Name, string? Email, string? EmployeeId, string? Designation, string? Phone, string RoleKey, string? Zone, int[]? StoreIds, int[]? SiteIds, bool Active);
public record RoleDto(string Key, string Label, string[] Perms, int SortOrder);
public record MetaDto(AppInfo App, bool LoginRequired, bool ShowUserPicker, bool FirstRun, DateTime ServerTime);

// ---- masters -----------------------------------------------------------------------------
public record VendorDto(int Id, string Code, string Name, string? Contact, string? Phone, string? Email, bool Active, int PurchaseOrders);
public record VendorUpsert(string Code, string Name, string? Contact, string? Phone, string? Email, bool Active);
public record PoLineDto(int Id, int LineNo, string Category, string SubCategory, string Spec, string Description, string Uom, decimal Qty, decimal Rate, int? ArticleId, string? ItemCode, decimal Received, decimal Issued);
public record PoDto(int Id, string Number, int VendorId, string VendorName, DateOnly Date, DateOnly? DeliveryDue, int WarrantyMonths, string Status, string? Remarks, decimal Value, List<PoLineDto> Lines);
public record PoLineUpsert(string Category, string SubCategory, string Spec, string Description, string Uom, decimal Qty, decimal Rate);
public record PoUpsertRequest(string Number, int VendorId, DateOnly Date, DateOnly? DeliveryDue, int WarrantyMonths, string? Remarks, List<PoLineUpsert> Lines);
public record ArticleDto(int Id, string ItemCode, string Category, string SubCategory, string Spec, string Description, string Uom, bool Serialised, decimal PackSize,
    int PurchaseOrderId, string PoNumber, int VendorId, string VendorName, decimal Rate, int WarrantyMonths, int? ExpectedLifeMonths, decimal ReorderLevel, string Status, int ItemsGenerated,
    decimal PoQty, decimal Received, decimal OnHand, decimal Reserved, decimal Installed, decimal Quarantine, DateTime CreatedAt);
public record ArticleCreateRequest(int PurchaseOrderLineId, bool Serialised, decimal PackSize, decimal ReorderLevel, int? ExpectedLifeMonths);
public record LocationDto(int Id, int StoreId, string StoreCode, string Section, string Rack, string Bin, string FullCode, string Kind, bool Active, int Items);
public record LocationUpsert(string Section, string Rack, string Bin, string Kind, bool Active);
public record StoreDto(int Id, string Code, string Name, string Type, string Zone, string? Address, double? Lat, double? Lng, string? InChargeUserId, string? InChargeName, bool Active, int Items, decimal Value, List<LocationDto> Locations);
public record StoreUpsert(string Code, string Name, string Type, string Zone, string? Address, double? Lat, double? Lng, string? InChargeUserId, bool Active);
public record SiteDto(int Id, string Code, string Name, string Zone, string Division, string? Ward, string Type, double Lat, double Lng, int GeofenceM,
    string? SupervisorUserId, string? SupervisorName, string? OfficerUserId, string? OfficerName, string Status, string? Remarks, int Installed, int AtSite, int Pending, DateTime CreatedAt);
public record SiteUpsert(string? Code, string Name, string Zone, string Division, string? Ward, string Type, double Lat, double Lng, int? GeofenceM, string? SupervisorUserId, string? OfficerUserId, string Status, string? Remarks);
public record BulkResultDto(int Created, int Updated, List<string> Errors);

// ---- items --------------------------------------------------------------------------------
public record ItemDto(Guid Id, string QrCode, int ArticleId, string ItemCode, string Description, string Category, int Serial, decimal Qty, string Uom, string Status,
    int? StoreId, string? StoreCode, int? LocationId, string? LocationCode, int? SiteId, string? SiteCode, string? SiteName,
    string PoNumber, string VendorName, decimal Rate, int WarrantyMonths, DateOnly? WarrantyExpires,
    int? GrnId, int? RequisitionId, int? IssueId, int? InstallationId, int? ReturnNoteId,
    DateTime? ReceivedAt, DateTime? IssuedAt, DateTime? InstalledAt, DateTime? ReturnedAt, int LabelPrints, DateTime UpdatedAt, string UpdatedBy);
public record ItemHistoryDto(DateTime At, string Action, string? By, string? Role, string? Reason, string? NextJson);
public record ItemDetailDto(ItemDto Item, List<ItemHistoryDto> History, InstallationDto? Installation, ReturnDto? Return);
public record ScanResultDto(bool Ok, string Message, ItemDto? Item, string? Step = null);

// ---- goods receipt ------------------------------------------------------------------------
public record GrnScanRequest(string Qr, int PurchaseOrderId, int StoreId);
/// <summary>StoreLocationId is ignored: receipts always land in the store's Receiving area and are put away by scan (FR-INV-010).</summary>
public record GrnPostRequest(int PurchaseOrderId, int StoreId, int? StoreLocationId, string[]? QrCodes, string? ChallanNo, string? InvoiceNo, DateOnly? InvoiceDate, string? Remarks, string? Condition = null, List<GrnReject>? Rejections = null, List<GrnLineQty>? Lines = null);
/// <summary>Receipt by quantity, without scanning: the server takes the article's next unreceived labels.</summary>
public record GrnLineQty(int ArticleId, decimal Qty, decimal RejectedQty = 0, string? RejectReason = null, string? RejectRemarks = null, string? RejectPhotoBase64 = null);
public record GrnReject(string Qr, string ReasonCode, string? Remarks, string? PhotoBase64);
public record GrnDto(int Id, string Number, int PurchaseOrderId, string PoNumber, string VendorName, int StoreId, string StoreCode, string? ChallanNo, string? InvoiceNo, DateOnly? InvoiceDate,
    string PostedBy, DateTime PostedAt, int ItemCount, decimal Qty, decimal Value, string? Remarks, bool Reversed, string? Condition, int RejectedCount);
public record DiscrepancyRequest(int PurchaseOrderId, int? ArticleId, int? GrnId, string Kind, decimal Qty, string Reason);
public record DiscrepancyDto(int Id, string Number, int PurchaseOrderId, string PoNumber, string VendorName, int? ArticleId, string? ItemCode, string Kind, decimal Qty, string Reason, string Status, DateTime RaisedAt, string? Resolution, DateTime? ResolvedAt);
public record ResolveRequest(string Resolution);

// ---- requisition --------------------------------------------------------------------------
public record ReqLineDto(int Id, int ArticleId, string ItemCode, string Description, string Uom, decimal QtyRequested, decimal QtyApproved, decimal QtyIssued, decimal Shortfall, decimal Available, bool IndentRaised);
public record RequisitionDto(int Id, string Number, int SiteId, string SiteCode, string SiteName, string Zone, int StoreId, string StoreCode, string RaisedBy, string RaisedById, DateTime RaisedAt, DateOnly? NeedBy,
    string Status, string? Purpose, string? ApprovedBy, DateTime? DecidedAt, string? DecisionNote, string? IssueSlipNo, decimal Value, List<ReqLineDto> Lines, List<int> IssueIds,
    int RequiredLevel, int CurrentLevel, string? RecommendedBy, DateTime? SubmittedAt, DateTime? LapsedAt);
public record ReqLineUpsert(int ArticleId, decimal Qty);
public record RequisitionUpsert(int SiteId, int StoreId, DateOnly? NeedBy, string? Purpose, List<ReqLineUpsert> Lines, bool Submit);
public record ReqLineDecision(int LineId, decimal QtyApproved);
public record RequisitionDecision(bool Approve, string? Note, List<ReqLineDecision>? Lines);

// ---- issuance -----------------------------------------------------------------------------
public record IssueScanRequest(string Qr, int RequisitionId);
public record IssuePostRequest(int RequisitionId, string[]? QrCodes, string? IssuedTo, string? Vehicle, string? Remarks, List<IssueLineQty>? Lines = null);
/// <summary>Issue by quantity, without scanning: the server picks the stock (this requisition's reservation first, then oldest receipt first).</summary>
public record IssueLineQty(int ArticleId, decimal Qty);
public record IssueAvailableDto(int ArticleId, string ItemCode, string Uom, decimal PackSize, decimal Reserved, decimal InStock, decimal InReceiving);
public record IssueDto(int Id, string Number, string GatePassNo, int RequisitionId, string RequisitionNo, int StoreId, string StoreCode, int SiteId, string SiteCode, string SiteName,
    string IssuedBy, DateTime IssuedAt, string? IssuedTo, string? Vehicle, string? ReceivedById, DateTime? ReceivedAt, int ItemCount, decimal Qty, decimal Value, string? Remarks, List<ItemDto> Items);
public record AckRequest(double? Lat, double? Lng, double? AccuracyM);

// ---- installation -------------------------------------------------------------------------
public record EvidenceDto(Guid Id, string Kind, string Name, string ContentType, long Size, double? Lat, double? Lng, double? AccuracyM, DateTime CapturedAt, string UploadedBy, DateTime UploadedAt, string Url,
    string? Sha256, string? StampedUrl, Guid? DeviceId);
public record InstallationDto(int Id, string Number, Guid ItemId, string QrCode, string ItemCode, string Description, int SiteId, string SiteCode, string SiteName, string Zone, double SiteLat, double SiteLng, int GeofenceM,
    string Officer, string OfficerId, string? PoleId, double Lat, double Lng, double AccuracyM, double DistanceM, string GeoStatus, string? Remarks, DateTime CapturedAt, DateTime SubmittedAt, bool Offline,
    string? VerifiedBy, DateTime? VerifiedAt, string? Justification, Guid? ReplacesItemId, EvidenceDto Before, EvidenceDto After,
    Guid? ClientTxnId, Guid? DeviceId, DateTime? QueuedAt, int AgeDays, int EscalationLevel, DateTime? EscalatedAt,
    string? FvRequestedBy, DateTime? FvRequestedAt, string? FvRequestNote, string? FvAssignedToId, string? FvFindings, string? FvFindingsBy, DateTime? FvFindingsAt, List<string> FvPhotoUrls);
public record VerifyRequest(bool Approve, string? Justification);

// ---- returns ------------------------------------------------------------------------------
public record ReturnReportRequest(string Qr, string Reason, string Kind, string? Remarks);
public record ReturnReceiveRequest(string Qr, int StoreId, int? StoreLocationId, string? Remarks);
public record ReturnClassifyRequest(string Classification, string? ClaimRef, string? Remarks, int? StoreLocationId = null, string? StoreLocationQr = null);
public record ReturnDisposeRequest(decimal? ScrapValue, string? Remarks);
public record ReturnDto(int Id, string Number, Guid ItemId, string QrCode, string ItemCode, string Description, string PoNumber, string VendorName, int? SiteId, string? SiteCode, string? SiteName,
    int? StoreId, string? StoreCode, string? LocationCode, string Reason, string Kind, string ReportedBy, DateTime ReportedAt, string? ReceivedBy, DateTime? ReceivedAt,
    string Classification, bool WarrantyEligible, DateOnly? WarrantyExpires, string? ClaimRef, DateTime? ClassifiedAt, DateTime? DisposalApprovedAt, decimal? ScrapValue, DateTime? ClosedAt,
    string? Remarks, DateTime? InstalledAt, int? ServiceDays, string ItemStatus);

// ---- stock, dashboard, reports, audit ----------------------------------------------------
public record StockRowDto(int ArticleId, string ItemCode, string Description, string Category, string Uom, int StoreId, string StoreCode, int LocationId, string LocationCode, string LocationKind,
    decimal OnHand, decimal Reserved, decimal Available, decimal Value, int OldestDays);
public record StockSummaryDto(List<StockRowDto> Rows, decimal TotalValue, decimal TotalQty, int Articles, int Items, List<ArticleDto> BelowReorder);
public record KpiDto(string Key, string Label, decimal Value, string? Sub, string Tone);
public record SeriesPoint(string Label, decimal Value, decimal? Value2 = null);
public record GroupRow(string Key, string Label, decimal A, decimal B, decimal C, decimal D);
public record DashboardDto(List<KpiDto> Kpis, List<SeriesPoint> Monthly, List<GroupRow> ByZone, List<GroupRow> ByCategory, List<InstallationDto> Exceptions, List<RequisitionDto> PendingApprovals,
    List<ReturnDto> OverdueReturns, List<IssueDto> UnacknowledgedIssues, Dictionary<string, int> ItemsByStatus, AppInfo App, DateTime ServerTime);
public record ReportDto(string Key, string Title, string[] Columns, List<object?[]> Rows, Dictionary<string, string> Filters);
public record AuditDto(long Id, DateTime At, string Kind, string Action, string? EntityType, string? EntityId, string? ActorId, string? ActorName, string? Role, string? PrevJson, string? NextJson, string? Reason, string? Ip);
