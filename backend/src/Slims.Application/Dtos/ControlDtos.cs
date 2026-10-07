namespace Slims.Application.Dtos;

// ---- put-away and bin moves (FR-INV-010/012) ------------------------------------------------------
public record MoveRequest(string[] QrCodes, int? ToLocationId, string? ToLocationQr, string? Remarks);
public record MoveResultDto(int Moved, string ToLocation, List<ItemDto> Items);
public record ScanLookupDto(string Kind, ItemDto? Item, LocationDto? Location, string Message);

// ---- inter-store transfer (FR-INV-019) ------------------------------------------------------------
public record TransferDespatchRequest(int FromStoreId, int ToStoreId, string[] QrCodes, string? Vehicle, string? Remarks);
public record TransferReceiveRequest(string[] QrCodes, string? Remarks);
public record TransferDto(int Id, string Number, int FromStoreId, string FromStoreCode, int ToStoreId, string ToStoreCode, string Status, string DespatchedBy, DateTime DespatchedAt, string? Vehicle, string? Remarks,
    int ItemCount, decimal Qty, int InTransit, string? ReceivedBy, DateTime? ReceivedAt, List<ItemDto> Items);

// ---- stock adjustment (FR-INV-020) ----------------------------------------------------------------
public record AdjustmentRequest(string Qr, string Kind, string ReasonCode, decimal? QtyAfter, int? LocationId, string? Remarks);
public record DecisionRequest(bool Approve, string? Note);
public record AdjustmentDto(int Id, string Number, int StoreId, string StoreCode, Guid ItemId, string QrCode, string ItemCode, string Description, int Serial, string Kind, string ReasonCode, string? Remarks,
    decimal QtyBefore, decimal QtyAfter, string Status, int? PhysicalCountId, string RaisedBy, string RaisedById, DateTime RaisedAt, string? DecidedBy, DateTime? DecidedAt, string? DecisionNote, string ItemStatus);

// ---- physical verification (FR-INV-021) -----------------------------------------------------------
public record CountStartRequest(int StoreId, int? LocationId);
public record CountScanRequest(string Qr, decimal? Qty, int? LocationId);
public record CountScanDto(string Qr, Guid? ItemId, string? ItemCode, int? Serial, decimal Qty, int? LocationId, string? LocationCode, DateTime At, string Result);
public record VarianceLineDto(string Kind, Guid? ItemId, string Qr, string? ItemCode, int? Serial, string? BookLocation, string? CountedLocation, decimal BookQty, decimal CountedQty, int? AdjustmentId, string? AdjustmentStatus, string Clearance);
public record CountDto(int Id, string Number, int StoreId, string StoreCode, int? LocationId, string? LocationCode, string Status, string StartedBy, DateTime StartedAt, DateTime? SubmittedAt, DateTime? ClosedAt,
    int BookItems, int Scanned, List<CountScanDto> Scans, List<VarianceLineDto> Variances);

// ---- amendments (FR-EXC-011) ----------------------------------------------------------------------
public record AmendmentCreate(string EntityType, string EntityRef, string Field, string? NewValue, string Reason);
public record AmendmentDto(int Id, string Number, string EntityType, string EntityRef, string Field, string? OldValue, string? NewValue, string Reason, string Status, string RaisedBy, string RaisedById, DateTime RaisedAt,
    string? DecidedBy, DateTime? DecidedAt, string? DecisionNote);
public record AmendableFieldDto(string EntityType, string Field, string Label);

// ---- field verification (FR-EXC-007) --------------------------------------------------------------
public record FieldVerifyRequest(string? Note);
