namespace Slims.Domain.Entities;

/// <summary>The quantity ledger (FR-INV-016): one row per quantity change at a store. Put-away and bin moves write a row with
/// both locations and no net effect. Reconciliation is Opening + Receipts + Returns + Transfers in − Issues − Transfers out
/// − Disposals ± Adjustments = Closing, per store and article.</summary>
public class StockMovement
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "";              // Opening | Receipt | ReceiptReversal | Move | Issue | Return | TransferOut | TransferIn | Adjustment | Disposal
    public Guid ItemId { get; set; }
    public int ArticleId { get; set; }
    public int StoreId { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public decimal Qty { get; set; }                    // signed effect on the store's quantity: + in, − out, 0 for a move
    public string? RefType { get; set; }
    public string? RefNo { get; set; }
    public string UserId { get; set; } = "";
}

/// <summary>Inter-store transfer (FR-INV-019): despatched by scan, in transit, received by scan at the other store.</summary>
public class StockTransfer
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int FromStoreId { get; set; }
    public int ToStoreId { get; set; }
    public string Status { get; set; } = "InTransit";   // InTransit | Received | PartReceived
    public string DespatchedById { get; set; } = "";
    public string DespatchedBy { get; set; } = "";
    public DateTime DespatchedAt { get; set; } = DateTime.UtcNow;
    public string? Vehicle { get; set; }
    public string? Remarks { get; set; }
    public int ItemCount { get; set; }
    public decimal Qty { get; set; }
    public string? ReceivedById { get; set; }
    public string? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public Store? FromStore { get; set; }
    public Store? ToStore { get; set; }
}

/// <summary>Stock adjustment (FR-INV-020): reason code, second-person approval, original and adjusted values kept.</summary>
public class StockAdjustment
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int StoreId { get; set; }
    public Guid ItemId { get; set; }
    public int ArticleId { get; set; }
    public int? LocationId { get; set; }
    public string Kind { get; set; } = "Loss";          // Loss | Found | Quantity
    public string ReasonCode { get; set; } = "";
    public string? Remarks { get; set; }
    public decimal QtyBefore { get; set; }
    public decimal QtyAfter { get; set; }
    public string Status { get; set; } = "Pending";     // Pending | Approved | Rejected
    public int? PhysicalCountId { get; set; }
    public string RaisedById { get; set; } = "";
    public string RaisedBy { get; set; } = "";
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public string? DecidedById { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public Item? Item { get; set; }
    public Store? Store { get; set; }
}

/// <summary>Physical verification by scanning (FR-INV-021). Lines are kept as JSON: what was scanned and the variance found.</summary>
public class PhysicalCount
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int StoreId { get; set; }
    public int? LocationId { get; set; }                // null = the whole store
    public string Status { get; set; } = "Open";        // Open | Submitted | Closed
    public string StartedById { get; set; } = "";
    public string StartedBy { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string ScansJson { get; set; } = "[]";       // [{qr, qty, locationId, at}]
    public string VarianceJson { get; set; } = "[]";
    public Store? Store { get; set; }
}

/// <summary>Correction to a completed record (FR-EXC-011): reason, approval by another user, before and after values.</summary>
public class AmendmentRequest
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string EntityType { get; set; } = "";        // installation | issue | grn | return
    public string EntityRef { get; set; } = "";         // the record's number
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";     // Pending | Approved | Rejected
    public string RaisedById { get; set; } = "";
    public string RaisedBy { get; set; } = "";
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public string? DecidedById { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>A phone bound to a named user (FR-SEC-009). Field evidence is accepted only from the user's active device.</summary>
public class Device
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string DeviceUid { get; set; } = "";
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
    public string Status { get; set; } = "Setup";       // Setup | AwaitingApproval | Active | Replaced | Revoked
    public string TokenHash { get; set; } = "";
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime? SetupCompletedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ApprovedById { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public Guid? SetupEvidenceId { get; set; }
    public double? SetupLat { get; set; }
    public double? SetupLng { get; set; }
    public double? SetupAccuracyM { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedById { get; set; }
    public string? Note { get; set; }
    public User? User { get; set; }
}

/// <summary>One-time pairing code shown as a QR on the Mobile setup page. Valid for a few minutes, usable once.</summary>
public class DeviceEnrolment
{
    public string Code { get; set; } = "";
    public string UserId { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? DeviceId { get; set; }
}

/// <summary>In-app notification (FR-NTF-001/002). E-mail and SMS copies are sent by the background worker where configured.</summary>
public class Notification
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Code { get; set; } = "";              // NTF-01 … NTF-17, NTF-DEV
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Link { get; set; }
    public string? EntityType { get; set; }
    public string? EntityRef { get; set; }
    public bool Mandatory { get; set; }                 // assigned work: cannot be switched off (FR-NTF-005)
    public int Level { get; set; }                      // 0 = first notice, 1+ = escalation
    public string DedupKey { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public string EmailStatus { get; set; } = "None";   // None | Pending | Sent | Failed | Skipped
    public string SmsStatus { get; set; } = "None";
    public string? DeliveryError { get; set; }
}

/// <summary>Automatic report delivery (FR-RPT-010).</summary>
public class ReportSchedule
{
    public int Id { get; set; }
    public string ReportKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Frequency { get; set; } = "Weekly";   // Daily | Weekly | Monthly
    public int DayOfWeek { get; set; } = 1;             // 0 = Sunday, for Weekly
    public int DayOfMonth { get; set; } = 1;            // for Monthly
    public int Hour { get; set; } = 8;                  // Pakistan time
    public string Period { get; set; } = "previous";    // previous = the period just ended; todate = month to date
    public string? Zone { get; set; }
    public int? StoreId { get; set; }
    public string RecipientUserIds { get; set; } = "";  // comma-separated
    public string RecipientEmails { get; set; } = "";   // comma-separated, for people without an account
    public bool Active { get; set; } = true;
    public string CreatedById { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

public class ReportRun
{
    public int Id { get; set; }
    public int? ScheduleId { get; set; }
    public string ReportKey { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime RanAt { get; set; } = DateTime.UtcNow;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Rows { get; set; }
    public string FilePath { get; set; } = "";
    public string Status { get; set; } = "Done";        // Done | Failed
    public string? Error { get; set; }
    public string RunAsUserId { get; set; } = "";
}
