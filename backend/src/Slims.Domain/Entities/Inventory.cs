namespace Slims.Domain.Entities;

/// <summary>Every physical item carries exactly one status at any moment. Transitions are in Rules/ItemLifecycle.</summary>
public enum ItemStatus
{
    Created, Received, Rejected, Reserved, Issued, AtSite, PendingVerification, Installed,
    OutOfOrder, Dismantled, Returned, UnderRepair, WarrantyClaim, Scrap, Disposed, Cancelled,
    InTransit, WrittenOff,
}

/// <summary>One QR label = one row. Serialised articles: one item per piece. Bulk articles: one item per pack, Qty = pack size.</summary>
public class Item
{
    public Guid Id { get; set; }
    public string QrCode { get; set; } = "";            // payload printed in the QR symbol; globally unique, never reused
    public int ArticleId { get; set; }
    public int Serial { get; set; }
    public decimal Qty { get; set; } = 1;
    public ItemStatus Status { get; set; } = ItemStatus.Created;
    public int? StoreLocationId { get; set; }
    public int? SiteId { get; set; }
    public int? GrnId { get; set; }
    public int? RequisitionId { get; set; }             // reservation
    public int? IssueId { get; set; }
    public int? InstallationId { get; set; }
    public int? ReturnNoteId { get; set; }
    public int? TransferId { get; set; }                // in transit between stores
    public DateTime? ReceivedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? InstalledAt { get; set; }
    public DateTime? ReturnedAt { get; set; }
    public int LabelPrints { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "";
    public Article? Article { get; set; }
    public StoreLocation? StoreLocation { get; set; }
    public Site? Site { get; set; }
    public Grn? Grn { get; set; }
}

public class Grn
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int PurchaseOrderId { get; set; }
    public int StoreId { get; set; }
    public string? ChallanNo { get; set; }
    public string? InvoiceNo { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public string PostedById { get; set; } = "";
    public string PostedBy { get; set; } = "";
    public DateTime PostedAt { get; set; } = DateTime.UtcNow;
    public int ItemCount { get; set; }
    public decimal Qty { get; set; }
    public decimal Value { get; set; }
    public string? Remarks { get; set; }
    public string? Condition { get; set; }              // physical condition of the consignment (FR-INV-005)
    public int RejectedCount { get; set; }
    public bool Reversed { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Store? Store { get; set; }
}

public class DiscrepancyNote
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int PurchaseOrderId { get; set; }
    public int? ArticleId { get; set; }
    public int? GrnId { get; set; }
    public string Kind { get; set; } = "Short";         // Short | Excess | Rejected
    public decimal Qty { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Open";        // Open | Resolved
    public string RaisedById { get; set; } = "";
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public string? Resolution { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Article? Article { get; set; }
}

public enum RequisitionStatus { Draft, Submitted, Approved, Rejected, PartIssued, Issued, Cancelled, Lapsed }

public class Requisition
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int SiteId { get; set; }
    public int StoreId { get; set; }
    public string RaisedById { get; set; } = "";
    public string RaisedBy { get; set; } = "";
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public DateOnly? NeedBy { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public string? Purpose { get; set; }
    public string? ApprovedById { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public string? IssueSlipNo { get; set; }
    public decimal Value { get; set; }                  // requested value at submission, for the approval matrix
    public int RequiredLevel { get; set; } = 1;         // FR-REQ-010: the level whose limit covers the value
    public int CurrentLevel { get; set; } = 1;          // the level that has to act now
    public string? RecommendedBy { get; set; }          // lower levels that passed it up, with dates
    public DateTime? SubmittedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime? LapseWarnedAt { get; set; }
    public DateTime? LapsedAt { get; set; }
    public Site? Site { get; set; }
    public Store? Store { get; set; }
    public List<RequisitionLine> Lines { get; set; } = new();
}

public class RequisitionLine
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public int ArticleId { get; set; }
    public decimal QtyRequested { get; set; }
    public decimal QtyApproved { get; set; }
    public decimal QtyIssued { get; set; }
    public decimal Shortfall { get; set; }
    public bool IndentRaised { get; set; }
    public Article? Article { get; set; }
}

public class Issue
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string GatePassNo { get; set; } = "";
    public int RequisitionId { get; set; }
    public int StoreId { get; set; }
    public int SiteId { get; set; }
    public string IssuedById { get; set; } = "";
    public string IssuedBy { get; set; } = "";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string? IssuedTo { get; set; }
    public string? Vehicle { get; set; }
    public string? ReceivedById { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public double? ReceivedLat { get; set; }
    public double? ReceivedLng { get; set; }
    public int ItemCount { get; set; }
    public decimal Qty { get; set; }
    public string? Remarks { get; set; }
    public Requisition? Requisition { get; set; }
    public Site? Site { get; set; }
    public Store? Store { get; set; }
}

/// <summary>A photograph with the coordinates and time it was taken. Immutable after upload.</summary>
public class Evidence
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "before";        // before | after | return | site
    public string Name { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public long Size { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public double? AccuracyM { get; set; }
    public DateTime CapturedAt { get; set; }
    public string UploadedById { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? Ip { get; set; }
    public string? Sha256 { get; set; }                 // of the original bytes, as sent by the device (FR-FLD-009)
    public string? StampedPath { get; set; }            // server-generated copy with the stamp overlay
    public Guid? DeviceId { get; set; }
    public bool IsMock { get; set; }
}

public enum GeoStatus { Confirmed, PendingVerification, ApprovedException, Rejected, MissingEvidence }

public class Installation
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public Guid ItemId { get; set; }
    public int SiteId { get; set; }
    public string OfficerId { get; set; } = "";
    public string Officer { get; set; } = "";
    public string? PoleId { get; set; }
    public Guid BeforeEvidenceId { get; set; }
    public Guid AfterEvidenceId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double AccuracyM { get; set; }
    public double DistanceM { get; set; }
    public GeoStatus GeoStatus { get; set; }
    public string? Remarks { get; set; }
    public DateTime CapturedAt { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public bool Offline { get; set; }
    public string? VerifiedById { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? Justification { get; set; }
    public Guid? ReplacesItemId { get; set; }
    public Guid? ClientTxnId { get; set; }              // FR-FLD-013: a repeated synchronisation returns the first result
    public Guid? DeviceId { get; set; }
    public DateTime? QueuedAt { get; set; }             // when the device queued it offline; SubmittedAt is the sync time
    public DateTime? DeviceTime { get; set; }           // the device clock at synchronisation, for the drift check
    // FR-EXC-007: field verification by the Lighting Supervisor
    public string? FvRequestedById { get; set; }
    public string? FvRequestedBy { get; set; }
    public DateTime? FvRequestedAt { get; set; }
    public string? FvRequestNote { get; set; }
    public string? FvAssignedToId { get; set; }
    public string? FvFindings { get; set; }
    public string? FvFindingsBy { get; set; }
    public DateTime? FvFindingsAt { get; set; }
    public string? FvEvidenceIds { get; set; }          // comma-separated evidence ids
    // FR-EXC-010: ageing and escalation
    public int EscalationLevel { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public Item? Item { get; set; }
    public Site? Site { get; set; }
    public Evidence? Before { get; set; }
    public Evidence? After { get; set; }
}

public enum ReturnClass { Pending, Repairable, Warranty, Scrap, Usable }

public class ReturnNote
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public Guid ItemId { get; set; }
    public int? SiteId { get; set; }
    public int? StoreId { get; set; }
    public int? StoreLocationId { get; set; }
    public string Reason { get; set; } = "";
    public string Kind { get; set; } = "OutOfOrder";    // OutOfOrder | Dismantled | Unused
    public string ReportedById { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
    public Guid? EvidenceId { get; set; }
    public string? ReceivedById { get; set; }
    public string? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public ReturnClass Classification { get; set; } = ReturnClass.Pending;
    public bool WarrantyEligible { get; set; }
    public DateOnly? WarrantyExpires { get; set; }
    public string? ClaimRef { get; set; }
    public string? ClassifiedById { get; set; }
    public DateTime? ClassifiedAt { get; set; }
    public string? DisposalApprovedById { get; set; }
    public DateTime? DisposalApprovedAt { get; set; }
    public decimal? ScrapValue { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Remarks { get; set; }
    public Item? Item { get; set; }
    public Site? Site { get; set; }
    public Store? Store { get; set; }
    public Evidence? Evidence { get; set; }
}
