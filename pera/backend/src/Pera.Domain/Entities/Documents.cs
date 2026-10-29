namespace Pera.Domain.Entities;

/// <summary>Stage type is kept separate from the action an officer takes (SRD 6).</summary>
public enum StageType { Information, Review, Concurrence, Approval, Signature, Dispatch }

public enum StageAction { Acknowledge, Comment, Recommend, Concur, NotConcur, Approve, Reject, Return, Sign, Dispatch, Record }

public enum StageStatus { Pending, Active, Done, Returned, Rejected, Cancelled }

public enum DocumentStatus { Draft, InRoute, Returned, Rejected, Withdrawn, Completed }

public enum ApprovalState { NotRequired, Pending, Approved }

public enum SignatureState { NotRequired, Pending, Signed, Invalidated }

public enum VersionState { Draft, Submitted, Superseded, Approved, Signed }

public enum Classification { Public, Internal, Confidential, Restricted }

public enum AssigneeMode { User, Designation, ReportingOfficer, Nominated }

public enum AuthorityKind { Concur, Approve, Sign }

public class DocumentType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Comma-separated, lower case, without dots: "pdf,docx".</summary>
    public string AllowedExtensions { get; set; } = "pdf,docx,xlsx,jpg,png";
    public int MaxSizeMb { get; set; } = 20;
    public bool RequireAttachment { get; set; } = true;
    /// <summary>Approve and sign only where the authority matrix grants the officer's designation that power.</summary>
    public bool EnforceAuthority { get; set; } = true;
    public bool AllowSelfApproval { get; set; }
    public bool AllowParallel { get; set; } = true;
    public bool AmendmentNeedsApproval { get; set; } = true;
    public bool RequireApprovalStage { get; set; } = true;
    public bool ApprovalCommentRequired { get; set; }
    public Classification DefaultClassification { get; set; } = Classification.Internal;
    /// <summary>Default turnaround per stage, hours, when a template stage does not set one.</summary>
    public int StageServiceHours { get; set; } = 48;
    public int? DefaultTemplateId { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Versioned route definition (FR-012). Publishing a change creates the next version; instances keep the
/// version they were submitted with plus a frozen snapshot.</summary>
public class WorkflowTemplate
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public int DocumentTypeId { get; set; }
    public string Status { get; set; } = "Draft";        // Draft | Active | Retired
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EffectiveTo { get; set; }
    public string OwnerId { get; set; } = "";
    /// <summary>Initiators may add or reorder optional stages and nominate officers (FR-013).</summary>
    public bool AllowCustomization { get; set; } = true;
    public DocumentType? DocumentType { get; set; }
    public List<WorkflowTemplateStage> Stages { get; set; } = [];
}

public class WorkflowTemplateStage
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    /// <summary>Stages with the same step run in parallel; steps run in ascending order.</summary>
    public int Step { get; set; }
    public string Name { get; set; } = "";
    public StageType Type { get; set; }
    public bool Mandatory { get; set; }
    public AssigneeMode AssigneeMode { get; set; } = AssigneeMode.Nominated;
    public string? UserId { get; set; }
    public int? DesignationId { get; set; }
    public int? ServiceHours { get; set; }
    public bool InitiatorMayChangeAssignee { get; set; } = true;
}

/// <summary>Delegation of powers: which designation may concur, approve or sign which document type, and when (BRD 11).</summary>
public class AuthorityRule
{
    public int Id { get; set; }
    public int? DocumentTypeId { get; set; }
    public int DesignationId { get; set; }
    public AuthorityKind Authority { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EffectiveTo { get; set; }
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
    public Designation? Designation { get; set; }
    public DocumentType? DocumentType { get; set; }

    public bool InForce(DateTime utc) => Active && utc.Date >= EffectiveFrom.Date && (EffectiveTo is null || utc.Date <= EffectiveTo.Value.Date);
}

/// <summary>A document case / matter moving through review, approval and signature (FR-009).</summary>
public class DocumentCase
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Subject { get; set; } = "";
    public int DocumentTypeId { get; set; }
    public int? OriginOrgUnitId { get; set; }
    public Classification Classification { get; set; } = Classification.Internal;
    public int? TaskId { get; set; }
    public string? ExternalRef { get; set; }
    public string? Notes { get; set; }
    public string InitiatorId { get; set; } = "";
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public ApprovalState ApprovalState { get; set; } = ApprovalState.Pending;
    public SignatureState SignatureState { get; set; } = SignatureState.NotRequired;
    public int? ActiveVersionId { get; set; }
    public int? TemplateId { get; set; }
    public int? TemplateVersion { get; set; }
    /// <summary>Frozen route at the latest submission (SRD 6).</summary>
    public string? RouteSnapshotJson { get; set; }
    public int ReturnCount { get; set; }
    public string? Outcome { get; set; }                 // Dispatched | On record | Rejected | Withdrawn
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DocumentType? DocumentType { get; set; }
    public User? Initiator { get; set; }
    public OrgUnit? OriginOrgUnit { get; set; }
    public WorkTask? Task { get; set; }
    public List<DocumentVersion> Versions { get; set; } = [];
    public List<RouteStage> Route { get; set; } = [];
    public List<Movement> Movements { get; set; } = [];
    public List<SignatureField> SignatureFields { get; set; } = [];
    public List<RouteAmendment> Amendments { get; set; } = [];
}

public class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int No { get; set; }
    public Guid FileId { get; set; }
    public VersionState State { get; set; } = VersionState.Draft;
    public string? Note { get; set; }
    public string CreatedById { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public StoredFile? File { get; set; }
}

/// <summary>One stage of a document's route (the workflow instance).</summary>
public class RouteStage
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int Step { get; set; }
    public string Name { get; set; } = "";
    public StageType Type { get; set; }
    public bool Mandatory { get; set; }
    public int? TemplateStageId { get; set; }
    public string? UserId { get; set; }
    /// <summary>Added ad hoc ("mark for information"); never blocks the route.</summary>
    public bool AdHoc { get; set; }
    public StageStatus Status { get; set; } = StageStatus.Pending;
    public int? ServiceHours { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? ActedById { get; set; }
    public string? ActingForId { get; set; }
    public StageAction? Outcome { get; set; }
    public string? Comment { get; set; }
    /// <summary>The version this stage received; actions on any other version are refused.</summary>
    public int? VersionId { get; set; }
    public int EscalationLevel { get; set; }
    public DateTime? RemindedAt { get; set; }
    public User? User { get; set; }

    public bool Blocking => !AdHoc && Type != StageType.Information;
}

/// <summary>Movement history (FR-020): sender, recipient, stage, action, version, time held.</summary>
public class Movement
{
    public long Id { get; set; }
    public int DocumentId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? FromUserId { get; set; }
    public string? ToUserId { get; set; }
    public string? ActingForId { get; set; }
    public int? RouteStageId { get; set; }
    public string? StageName { get; set; }
    public string Action { get; set; } = "";
    public string? ActionRequested { get; set; }
    public string? Comment { get; set; }
    public int? VersionNo { get; set; }
    public double? HeldHours { get; set; }
}

/// <summary>A controlled change to a submitted route (FR-014): reason, before/after and, where configured, approval.</summary>
public class RouteAmendment
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string RequestedById { get; set; } = "";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";      // Pending | Applied | Rejected
    public string ProposalJson { get; set; } = "[]";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? DecidedById { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

public enum SignatureFieldStatus { Draft, Pending, Signed, Invalidated }

/// <summary>Where a signature goes and, once signed, the evidence (FR-018/019, SRD 5).
/// Coordinates are normalised 0–1 from the top-left of the page so they survive any page size.</summary>
public class SignatureField
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? RouteStageId { get; set; }
    public string SignerUserId { get; set; } = "";
    public int Order { get; set; }
    public int Page { get; set; } = 1;
    public double X { get; set; } = 0.62;
    public double Y { get; set; } = 0.82;
    public double W { get; set; } = 0.3;
    public double H { get; set; } = 0.09;
    public bool ShowName { get; set; } = true;
    public bool ShowDate { get; set; } = true;
    public SignatureFieldStatus Status { get; set; } = SignatureFieldStatus.Draft;

    public int? VersionId { get; set; }
    public string? DocumentHash { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignedById { get; set; }
    public string? ActingForId { get; set; }
    public string? AuthMethod { get; set; }
    public string? SignatureCode { get; set; }
    public string? SignatureMac { get; set; }
    public Guid? SignedFileId { get; set; }
    public string? InvalidatedReason { get; set; }
}

public class EscalationLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string EntityType { get; set; } = "";         // task | stage
    public string EntityId { get; set; } = "";
    public string Reference { get; set; } = "";
    public int Level { get; set; }                       // 0 = reminder, 1 = first escalation, 2 = second
    public string Recipients { get; set; } = "";
    public string Reason { get; set; } = "";
}
